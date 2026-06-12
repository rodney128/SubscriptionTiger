using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class OutlookScanService : IOutlookScanService
{
    private const int LookbackMonths = 15;
    private const int MaxMatchingMessages = 3000;
    private const int PageSize = 100;
    private const string OutlookSelectFields = "$select=id,subject,from,receivedDateTime,bodyPreview,conversationId";

    private static readonly string[] OutlookBillingTerms =
    [
        "receipt", "invoice", "subscription", "renewal", "payment",
        "charged", "membership", "plan", "billing"
    ];

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
            diagnosticsService.RecordEvent("OutlookScan", "Starting Outlook message query");
            diagnosticsService.RecordEvent("OutlookScan", "Scanning Outlook messages...");
            var scanData = await GetCandidatesAsync(authResult.AccessToken, cancellationToken).ConfigureAwait(false);
            return new OutlookScanResult(
                IsConfigured: true,
                ScanMode: "Real Outlook read-only scan",
                MessagesChecked: scanData.MessagesChecked,
                Candidates: scanData.Candidates,
                AccessToken: authResult.AccessToken,
                ResultMessage: $"Outlook scan completed. Checked {scanData.MessagesChecked} messages. Found {scanData.Candidates.Count} suspected subscriptions.",
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
                ResultMessage: "Outlook API request failed.",
                ScanTime: DateTime.Now);
        }
    }

    public async Task<EmailBodyContent?> GetMessageContentAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var authResult = await authService.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        if (!authResult.IsSuccess || string.IsNullOrWhiteSpace(authResult.AccessToken))
        {
            return null;
        }

        var requestUri = $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(messageId)}?$select=body,bodyPreview";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = json.RootElement;
        var bodyPreview = TryGetString(root, "bodyPreview");

        string? html = null;
        string? plainText = null;

        if (root.TryGetProperty("body", out var bodyElement)
            && bodyElement.ValueKind == JsonValueKind.Object)
        {
            var contentType = TryGetString(bodyElement, "contentType");
            var content = TryGetString(bodyElement, "content");

            if (!string.IsNullOrWhiteSpace(content))
            {
                if (string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase))
                {
                    html = content;
                }
                else
                {
                    plainText = content;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(plainText))
        {
            plainText = bodyPreview;
        }

        if (string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        return new EmailBodyContent(html, plainText, IsFullBody: true);
    }

    private async Task<(int MessagesChecked, IReadOnlyList<SubscriptionCandidate> Candidates)> GetCandidatesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var lookbackStart = DateTimeOffset.UtcNow.AddMonths(-LookbackMonths).Date;
        var lookbackIso = lookbackStart.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);
        var filter = $"$filter=receivedDateTime ge {lookbackIso}";
        var top = $"$top={PageSize}";
        var orderBy = "$orderby=receivedDateTime desc";
        var initialUri = $"https://graph.microsoft.com/v1.0/me/messages?{top}&{OutlookSelectFields}&{filter}&{orderBy}";
        var scanLimit = MaxMatchingMessages;
        diagnosticsService.RecordEvent("OutlookScan", $"Using lookback={LookbackMonths}m, target={scanLimit}, filter={filter}");

        var pagedMessages = new List<JsonElement>(scanLimit);
        var nextUrl = initialUri;

        while (!string.IsNullOrWhiteSpace(nextUrl) && pagedMessages.Count < scanLimit)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (json.RootElement.TryGetProperty("value", out var messages)
                && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    if (pagedMessages.Count >= scanLimit)
                    {
                        break;
                    }

                    pagedMessages.Add(message.Clone());
                }
            }

            if (!json.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkElement)
                || nextLinkElement.ValueKind != JsonValueKind.String)
            {
                break;
            }

            nextUrl = nextLinkElement.GetString();
        }

        if (pagedMessages.Count == 0)
        {
            return (0, Array.Empty<SubscriptionCandidate>());
        }

        var candidates = new List<SubscriptionCandidate>();
        var checkedCount = 0;

        foreach (var message in pagedMessages)
        {
            checkedCount++;
            diagnosticsService.RecordEvent("OutlookScan", $"Scanning Outlook messages... checked {checkedCount} of {pagedMessages.Count}");
            diagnosticsService.RecordEvent("OutlookScan", $"Outlook messages checked: {checkedCount} of {pagedMessages.Count}");

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
            var messageId = TryGetString(message, "id");

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
                SourceEmailSnippet: bodyPreview,
                SourceMessageId: messageId));
        }

        var enrichedCandidates = signalAnalyzer.ApplyRecurringEvidence(candidates);
        var monthlyRecurring = enrichedCandidates.Count(x => x.BillingCycle == BillingCycle.Monthly && x.DetectionReason.Contains("Recurring monthly pattern", StringComparison.OrdinalIgnoreCase));
        var annualRecurring = enrichedCandidates.Count(x => x.BillingCycle == BillingCycle.Yearly && x.DetectionReason.Contains("annual", StringComparison.OrdinalIgnoreCase));
        var datedCandidates = enrichedCandidates.Where(x => x.SourceEmailDate.HasValue).Select(x => x.SourceEmailDate!.Value).ToList();
        if (datedCandidates.Count > 0)
        {
            diagnosticsService.RecordEvent("OutlookScan", $"Candidate date window oldest={datedCandidates.Min():yyyy-MM-dd} newest={datedCandidates.Max():yyyy-MM-dd}");
        }

        diagnosticsService.RecordEvent("OutlookScan", $"Outlook scan completed: checked={checkedCount} found={enrichedCandidates.Count} duplicates=0");
        diagnosticsService.RecordEvent("OutlookScan", $"Recurring summary monthly={monthlyRecurring} annual={annualRecurring}");

        return (checkedCount, enrichedCandidates);
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
