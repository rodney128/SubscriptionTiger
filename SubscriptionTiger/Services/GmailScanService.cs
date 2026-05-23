using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class GmailScanService : IGmailScanService
{
    private static readonly string[] DetectionTerms =
    [
        "subscription",
        "renewal",
        "receipt",
        "invoice",
        "payment",
        "membership",
        "premium",
        "billed",
        "charge"
    ];

    private readonly IGmailAuthService authService;
    private readonly HttpClient httpClient;

    public GmailScanService(IGmailAuthService authService, HttpClient httpClient)
    {
        this.authService = authService;
        this.httpClient = httpClient;
    }

    public async Task<GmailScanResult> ScanInboxAsync(CancellationToken cancellationToken)
    {
        var authResult = await authService.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        if (!authResult.IsConfigured)
        {
            return new GmailScanResult(
                IsConfigured: false,
                ScanMode: authResult.ScanMode,
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                AccessToken: null,
                ResultMessage: authResult.ResultMessage,
                ScanTime: DateTime.Now);
        }

        if (!authResult.IsSuccess || string.IsNullOrWhiteSpace(authResult.AccessToken))
        {
            return new GmailScanResult(
                IsConfigured: true,
                ScanMode: authResult.ScanMode,
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                AccessToken: null,
                ResultMessage: authResult.ResultMessage,
                ScanTime: DateTime.Now);
        }

        try
        {
            var matchedCandidates = await GetCandidatesFromGmailAsync(authResult.AccessToken, cancellationToken).ConfigureAwait(false);
            return new GmailScanResult(
                IsConfigured: true,
                ScanMode: "Real Gmail read-only scan",
                MessagesChecked: matchedCandidates.MessagesChecked,
                Candidates: matchedCandidates.Candidates,
                AccessToken: authResult.AccessToken,
                ResultMessage: "Gmail scan completed.",
                ScanTime: DateTime.Now);
        }
        catch (Exception ex)
        {
            return new GmailScanResult(
                IsConfigured: true,
                ScanMode: "Real Gmail read-only scan",
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                AccessToken: authResult.AccessToken,
                ResultMessage: $"Gmail API scan failed: {ex.Message}",
                ScanTime: DateTime.Now);
        }
    }

    private async Task<(int MessagesChecked, IReadOnlyList<SubscriptionCandidate> Candidates)> GetCandidatesFromGmailAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=25&q=subscription%20OR%20renewal%20OR%20receipt%20OR%20invoice%20OR%20payment%20OR%20membership%20OR%20premium%20OR%20billed%20OR%20charge");

        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken).ConfigureAwait(false);
        listResponse.EnsureSuccessStatusCode();

        await using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var listJson = await JsonDocument.ParseAsync(listStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!listJson.RootElement.TryGetProperty("messages", out var messagesElement)
            || messagesElement.ValueKind != JsonValueKind.Array)
        {
            return (0, Array.Empty<SubscriptionCandidate>());
        }

        var candidates = new List<SubscriptionCandidate>();
        var checkedCount = 0;

        foreach (var message in messagesElement.EnumerateArray())
        {
            if (!message.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            var messageId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(messageId))
            {
                continue;
            }

            checkedCount++;
            var candidate = await TryCreateCandidateAsync(messageId, accessToken, cancellationToken).ConfigureAwait(false);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return (checkedCount, candidates);
    }

    private async Task<SubscriptionCandidate?> TryCreateCandidateAsync(
        string messageId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{messageId}?format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var messageJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var snippet = messageJson.RootElement.TryGetProperty("snippet", out var snippetElement)
            ? snippetElement.GetString() ?? string.Empty
            : string.Empty;

        var headers = GetHeaders(messageJson.RootElement);
        var subject = headers.TryGetValue("Subject", out var subjectValue) ? subjectValue : "(No Subject)";
        var sender = headers.TryGetValue("From", out var fromValue) ? fromValue : "Unknown Sender";
        var dateRaw = headers.TryGetValue("Date", out var dateValue) ? dateValue : null;

        var textToEvaluate = $"{subject} {snippet} {sender}";
        if (!ContainsDetectionTerms(textToEvaluate))
        {
            return null;
        }

        var sourceDate = TryParseDate(dateRaw);
        var vendor = DeriveVendorName(sender);
        var confidence = CalculateConfidence(subject, snippet);

        return new SubscriptionCandidate(
            Guid.NewGuid(),
            vendor,
            Price: null,
            BillingCycle.Monthly,
            confidence,
            SubscriptionSource.Gmail,
            "Matched Gmail message against subscription-related terms.",
            SourceEmailSubject: subject,
            SourceEmailSender: sender,
            SourceEmailDate: sourceDate,
            SourceEmailSnippet: snippet);
    }

    private static Dictionary<string, string> GetHeaders(JsonElement messageRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!messageRoot.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("headers", out var headers)
            || headers.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (!header.TryGetProperty("name", out var nameElement)
                || !header.TryGetProperty("value", out var valueElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            var value = valueElement.GetString();
            if (!string.IsNullOrWhiteSpace(name) && value is not null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static bool ContainsDetectionTerms(string value)
    {
        return DetectionTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static int CalculateConfidence(string subject, string snippet)
    {
        var points = 50;

        if (ContainsDetectionTerms(subject))
        {
            points += 25;
        }

        if (ContainsDetectionTerms(snippet))
        {
            points += 20;
        }

        return Math.Clamp(points, 0, 99);
    }

    private static DateTimeOffset? TryParseDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return null;
        }

        return DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private static string DeriveVendorName(string sender)
    {
        var atIndex = sender.IndexOf('@');
        if (atIndex >= 0)
        {
            var domainPart = sender[(atIndex + 1)..];
            var domain = domainPart.Split('>', '<', ' ', '.')[0];
            if (!string.IsNullOrWhiteSpace(domain))
            {
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(domain);
            }
        }

        return sender.Length > 80 ? sender[..80] : sender;
    }
}
