using System.Net.Http.Headers;
using System.Text.Json;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class GmailScanService : IGmailScanService
{
    private const string GmailSearchQuery = "(subscription OR subscribed OR renewal OR renews OR billing OR invoice OR receipt OR payment OR paid OR charged OR plan OR membership OR trial OR \"free trial\" OR cancel OR \"manage subscription\" OR \"upcoming payment\" OR monthly OR yearly OR annual)";

    private readonly IGmailAuthService authService;
    private readonly HttpClient httpClient;
    private readonly DiagnosticsService diagnosticsService;
    private readonly SubscriptionSignalAnalyzer signalAnalyzer;

    public GmailScanService(
        IGmailAuthService authService,
        HttpClient httpClient,
        DiagnosticsService diagnosticsService,
        SubscriptionSignalAnalyzer signalAnalyzer)
    {
        this.authService = authService;
        this.httpClient = httpClient;
        this.diagnosticsService = diagnosticsService;
        this.signalAnalyzer = signalAnalyzer;
    }

    public async Task<GmailScanResult> ScanInboxAsync(CancellationToken cancellationToken)
    {
        diagnosticsService.RecordGmailOAuthStatus("Gmail scan started");
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

        diagnosticsService.RecordGmailOAuthStatus("Google OAuth returned success");

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
        diagnosticsService.RecordEvent("GmailScan", $"Using query: {GmailSearchQuery}");

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=40&q={Uri.EscapeDataString(GmailSearchQuery)}");

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

        if (!signalAnalyzer.TryAnalyze(
                sender,
                subject,
                snippet,
                out var confidence,
                out var reason,
                out var price,
                out var billingCycle,
                out var vendor))
        {
            return null;
        }

        var sourceDate = TryParseDate(dateRaw);

        return new SubscriptionCandidate(
            Guid.NewGuid(),
            vendor,
            Price: price,
            billingCycle,
            confidence,
            SubscriptionSource.Gmail,
            reason,
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

    private static DateTimeOffset? TryParseDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return null;
        }

        return DateTimeOffset.TryParse(rawDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }
}
