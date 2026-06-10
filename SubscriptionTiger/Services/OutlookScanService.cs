using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class OutlookScanService : IOutlookScanService
{
    private const string OutlookMessageEndpoint = "https://graph.microsoft.com/v1.0/me/messages?$top=40&$select=subject,from,receivedDateTime,bodyPreview";

    private readonly IOutlookAuthService authService;
    private readonly HttpClient httpClient;
    private readonly DiagnosticsService diagnosticsService;
    private readonly SubscriptionSignalAnalyzer signalAnalyzer;

    public OutlookScanService(
        IOutlookAuthService authService,
        HttpClient httpClient,
        DiagnosticsService diagnosticsService,
        SubscriptionSignalAnalyzer signalAnalyzer)
    {
        this.authService = authService;
        this.httpClient = httpClient;
        this.diagnosticsService = diagnosticsService;
        this.signalAnalyzer = signalAnalyzer;
    }

    public async Task<OutlookScanResult> ScanInboxAsync(CancellationToken cancellationToken)
    {
        diagnosticsService.RecordEvent("OutlookScan", "Outlook scan started");
        var authResult = await authService.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        if (!authResult.IsConfigured)
        {
            return new OutlookScanResult(
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
            return new OutlookScanResult(
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
            var scanData = await GetCandidatesAsync(authResult.AccessToken, cancellationToken).ConfigureAwait(false);
            return new OutlookScanResult(
                IsConfigured: true,
                ScanMode: "Real Outlook read-only scan",
                MessagesChecked: scanData.MessagesChecked,
                Candidates: scanData.Candidates,
                AccessToken: authResult.AccessToken,
                ResultMessage: "Outlook scan completed.",
                ScanTime: DateTime.Now);
        }
        catch (Exception ex)
        {
            diagnosticsService.RecordEvent("OutlookScan", $"Outlook Graph scan failed: {ex.GetType().Name}: {ex.Message}");
            return new OutlookScanResult(
                IsConfigured: true,
                ScanMode: "Real Outlook read-only scan",
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                AccessToken: authResult.AccessToken,
                ResultMessage: $"Outlook Graph scan failed: {ex.Message}",
                ScanTime: DateTime.Now);
        }
    }

    private async Task<(int MessagesChecked, IReadOnlyList<SubscriptionCandidate> Candidates)> GetCandidatesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        diagnosticsService.RecordEvent("OutlookScan", $"Using endpoint: {OutlookMessageEndpoint}");

        using var request = new HttpRequestMessage(HttpMethod.Get, OutlookMessageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("value", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            return (0, Array.Empty<SubscriptionCandidate>());
        }

        var candidates = new List<SubscriptionCandidate>();
        var checkedCount = 0;

        foreach (var message in messages.EnumerateArray())
        {
            checkedCount++;

            var subject = TryGetString(message, "subject") ?? "(No Subject)";
            var bodyPreview = TryGetString(message, "bodyPreview") ?? string.Empty;
            var sender = TryGetFrom(message);

            if (!signalAnalyzer.TryAnalyze(
                    sender,
                    subject,
                    bodyPreview,
                    out var confidence,
                    out var reason,
                    out var price,
                    out var billingCycle,
                    out var vendor))
            {
                continue;
            }

            var received = TryGetDateTimeOffset(message, "receivedDateTime");

            candidates.Add(new SubscriptionCandidate(
                Guid.NewGuid(),
                vendor,
                price,
                billingCycle,
                confidence,
                SubscriptionSource.Outlook,
                reason,
                SourceEmailSubject: subject,
                SourceEmailSender: sender,
                SourceEmailDate: received,
                SourceEmailSnippet: bodyPreview));
        }

        return (checkedCount, candidates);
    }

    private static string TryGetFrom(JsonElement message)
    {
        if (!message.TryGetProperty("from", out var fromElement)
            || fromElement.ValueKind != JsonValueKind.Object
            || !fromElement.TryGetProperty("emailAddress", out var emailAddressElement)
            || emailAddressElement.ValueKind != JsonValueKind.Object)
        {
            return "Unknown Sender";
        }

        var name = TryGetString(emailAddressElement, "name");
        var address = TryGetString(emailAddressElement, "address");

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(address))
        {
            return $"{name} <{address}>";
        }

        return !string.IsNullOrWhiteSpace(address)
            ? address
            : (name ?? "Unknown Sender");
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        var raw = TryGetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }
}
