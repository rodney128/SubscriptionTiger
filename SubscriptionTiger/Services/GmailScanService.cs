using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class GmailScanService : IGmailScanService
{
    private const int MaxMatchingMessagesCap = 3000;
    private const int PageSize = 100;
    private const int MetadataFetchConcurrency = 8;
    private const int MetadataBatchSize = 50;
    private const string GmailBillingQuery = "newer_than:15m (receipt OR invoice OR subscription OR renewal OR payment OR charged OR membership OR plan OR billing)";

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
            diagnosticsService.RecordGmailOAuthStatus("Searching Gmail...");
            var matchedCandidates = await GetCandidatesFromGmailAsync(authResult.AccessToken, cancellationToken).ConfigureAwait(false);
            diagnosticsService.RecordGmailOAuthStatus($"Checking matching messages {matchedCandidates.MessagesChecked} of {matchedCandidates.MessagesChecked}...");

            var capNote = matchedCandidates.CapReached
                ? $" Scan cap reached at {MaxMatchingMessagesCap} matching messages."
                : string.Empty;
            var resultMessage = $"Gmail scan completed. Matching messages found: {matchedCandidates.TotalMatchingMessages}. Checked {matchedCandidates.MessagesChecked} messages. Found {matchedCandidates.Candidates.Count} suspected subscriptions.{capNote}";

            return new GmailScanResult(
                IsConfigured: true,
                ScanMode: "Real Gmail read-only scan",
                MessagesChecked: matchedCandidates.MessagesChecked,
                Candidates: matchedCandidates.Candidates,
                AccessToken: authResult.AccessToken,
                ResultMessage: resultMessage,
                ScanTime: DateTime.Now);
        }
        catch (Exception ex)
        {
            diagnosticsService.RecordGmailOAuthStatus($"Gmail API failed: {ex.GetType().Name}: {ex.Message}");
            return new GmailScanResult(
                IsConfigured: true,
                ScanMode: "Real Gmail read-only scan",
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                AccessToken: authResult.AccessToken,
                ResultMessage: "Gmail API request failed.",
                ScanTime: DateTime.Now);
        }
    }

    public async Task<string?> GetMessageBodyAsync(string messageId, CancellationToken cancellationToken)
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

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{messageId}?format=full");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var messageJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!messageJson.RootElement.TryGetProperty("payload", out var payload))
        {
            return null;
        }

        return ExtractReadableBody(payload);
    }

    private async Task<(int MessagesChecked, int TotalMatchingMessages, bool CapReached, IReadOnlyList<SubscriptionCandidate> Candidates)> GetCandidatesFromGmailAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var query = GmailBillingQuery;
        diagnosticsService.RecordEvent("GmailScan", $"Using deep Gmail query with cap={MaxMatchingMessagesCap}: {query}");

        var messageIds = new List<string>(Math.Min(MaxMatchingMessagesCap, PageSize * 2));
        var pageToken = string.Empty;
        var totalMatchingMessages = 0;
        var capReached = false;

        diagnosticsService.RecordGmailOAuthStatus("Searching Gmail...");

        while (messageIds.Count < MaxMatchingMessagesCap)
        {
            var requestUri = string.IsNullOrWhiteSpace(pageToken)
                ? $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={PageSize}&q={Uri.EscapeDataString(query)}"
                : $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={PageSize}&q={Uri.EscapeDataString(query)}&pageToken={Uri.EscapeDataString(pageToken)}";

            using var listRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
            listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken).ConfigureAwait(false);
            listResponse.EnsureSuccessStatusCode();

            await using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var listJson = await JsonDocument.ParseAsync(listStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (listJson.RootElement.TryGetProperty("resultSizeEstimate", out var estimateElement)
                && estimateElement.ValueKind == JsonValueKind.Number
                && estimateElement.TryGetInt32(out var estimateValue)
                && estimateValue > 0)
            {
                totalMatchingMessages = Math.Max(totalMatchingMessages, estimateValue);
            }

            if (listJson.RootElement.TryGetProperty("messages", out var messagesElement)
                && messagesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesElement.EnumerateArray())
                {
                    if (messageIds.Count >= MaxMatchingMessagesCap)
                    {
                        capReached = true;
                        break;
                    }

                    if (!message.TryGetProperty("id", out var idElement))
                    {
                        continue;
                    }

                    var messageId = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(messageId))
                    {
                        messageIds.Add(messageId);
                    }
                }
            }

            if (capReached)
            {
                diagnosticsService.RecordGmailOAuthStatus($"Searching Gmail... cap reached at {MaxMatchingMessagesCap} matching messages.");
                break;
            }

            if (!listJson.RootElement.TryGetProperty("nextPageToken", out var nextTokenElement)
                || nextTokenElement.ValueKind != JsonValueKind.String)
            {
                break;
            }

            pageToken = nextTokenElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pageToken))
            {
                break;
            }
        }

        if (totalMatchingMessages < messageIds.Count)
        {
            totalMatchingMessages = messageIds.Count;
        }

        if (messageIds.Count == 0)
        {
            return (0, totalMatchingMessages, false, Array.Empty<SubscriptionCandidate>());
        }

        var candidates = new List<SubscriptionCandidate>();
        var checkedCount = 0;

        for (var batchStart = 0; batchStart < messageIds.Count; batchStart += MetadataBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchCount = Math.Min(MetadataBatchSize, messageIds.Count - batchStart);
            var batchIds = messageIds.GetRange(batchStart, batchCount);

            IReadOnlyList<SubscriptionCandidate?> batchResults;
            try
            {
                batchResults = await FetchCandidatesBatchAsync(batchIds, accessToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnosticsService.RecordEvent("GmailScan", $"Batch metadata fetch failed ({ex.GetType().Name}); falling back to per-message for {batchCount} messages.");
                batchResults = await FetchCandidatesPerMessageAsync(batchIds, accessToken, cancellationToken).ConfigureAwait(false);
            }

            foreach (var candidate in batchResults)
            {
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            checkedCount += batchCount;
            diagnosticsService.RecordGmailOAuthStatus($"Checking matching messages {checkedCount} of {messageIds.Count}...");
        }

        var enrichedCandidates = signalAnalyzer.ApplyRecurringEvidence(candidates);
        var monthlyRecurring = enrichedCandidates.Count(x => x.BillingCycle == BillingCycle.Monthly && x.DetectionReason.Contains("Recurring monthly pattern", StringComparison.OrdinalIgnoreCase));
        var annualRecurring = enrichedCandidates.Count(x => x.BillingCycle == BillingCycle.Yearly && x.DetectionReason.Contains("annual", StringComparison.OrdinalIgnoreCase));
        var datedCandidates = enrichedCandidates.Where(x => x.SourceEmailDate.HasValue).Select(x => x.SourceEmailDate!.Value).ToList();
        if (datedCandidates.Count > 0)
        {
            diagnosticsService.RecordEvent("GmailScan", $"Candidate date window oldest={datedCandidates.Min():yyyy-MM-dd} newest={datedCandidates.Max():yyyy-MM-dd}");
        }

        diagnosticsService.RecordGmailOAuthStatus(capReached
            ? $"Gmail scan completed: cap reached ({MaxMatchingMessagesCap}). Checked {checkedCount} of {messageIds.Count}."
            : $"Gmail scan completed: checked {checkedCount} of {messageIds.Count} matching messages.");
        diagnosticsService.RecordEvent("GmailScan", $"Recurring summary monthly={monthlyRecurring} annual={annualRecurring}");

        return (checkedCount, totalMatchingMessages, capReached, enrichedCandidates);
    }

    private async Task<IReadOnlyList<SubscriptionCandidate?>> FetchCandidatesBatchAsync(
        IReadOnlyList<string> messageIds,
        string accessToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var boundary = "batch_" + Guid.NewGuid().ToString("N");
        var bodyBuilder = new StringBuilder();
        for (var i = 0; i < messageIds.Count; i++)
        {
            bodyBuilder.Append("--").Append(boundary).Append("\r\n");
            bodyBuilder.Append("Content-Type: application/http\r\n");
            bodyBuilder.Append("Content-ID: <item-").Append(i).Append(">\r\n\r\n");
            bodyBuilder.Append("GET /gmail/v1/users/me/messages/").Append(messageIds[i])
                .Append("?format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date\r\n\r\n");
        }

        bodyBuilder.Append("--").Append(boundary).Append("--\r\n");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/batch/gmail/v1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(bodyBuilder.ToString(), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
        request.Content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseBoundary = response.Content.Headers.ContentType?.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"');

        if (string.IsNullOrWhiteSpace(responseBoundary))
        {
            throw new InvalidOperationException("Gmail batch response did not contain a multipart boundary.");
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var results = ParseBatchCandidates(responseText, responseBoundary);

        if (messageIds.Count > 0 && results.Count == 0)
        {
            throw new InvalidOperationException("Gmail batch response contained no parseable message parts.");
        }

        return results;
    }

    private List<SubscriptionCandidate?> ParseBatchCandidates(string responseText, string boundary)
    {
        var results = new List<SubscriptionCandidate?>();
        var delimiter = "--" + boundary;
        var segments = responseText.Split(new[] { delimiter }, StringSplitOptions.None);

        foreach (var segment in segments)
        {
            var trimmed = segment.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var jsonStart = segment.IndexOf('{');
            if (jsonStart < 0)
            {
                continue;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(segment.Substring(jsonStart));
                var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
                using var doc = JsonDocument.ParseValue(ref reader);
                results.Add(CreateCandidateFromMessageElement(doc.RootElement, string.Empty));
            }
            catch (JsonException)
            {
                // Skip a single unparseable part; remaining parts are still processed.
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<SubscriptionCandidate?>> FetchCandidatesPerMessageAsync(
        IReadOnlyList<string> messageIds,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var results = new List<SubscriptionCandidate?>(messageIds.Count);

        for (var start = 0; start < messageIds.Count; start += MetadataFetchConcurrency)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(MetadataFetchConcurrency, messageIds.Count - start);
            var tasks = new Task<SubscriptionCandidate?>[count];
            for (var offset = 0; offset < count; offset++)
            {
                tasks[offset] = TryCreateCandidateAsync(messageIds[start + offset], accessToken, cancellationToken);
            }

            var batch = await Task.WhenAll(tasks).ConfigureAwait(false);
            results.AddRange(batch);
        }

        return results;
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

        return CreateCandidateFromMessageElement(messageJson.RootElement, messageId);
    }

    private SubscriptionCandidate? CreateCandidateFromMessageElement(JsonElement messageRoot, string fallbackMessageId)
    {
        var snippet = messageRoot.TryGetProperty("snippet", out var snippetElement)
            ? snippetElement.GetString() ?? string.Empty
            : string.Empty;

        var headers = GetHeaders(messageRoot);
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
        var messageId = messageRoot.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? fallbackMessageId
            : fallbackMessageId;

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
            SourceEmailSnippet: snippet,
            SourceMessageId: messageId,
            SourceThreadId: messageRoot.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetString()
                : null);
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

    private static string? ExtractReadableBody(JsonElement payload)
    {
        var plainText = FindBodyByMimeType(payload, "text/plain");
        if (!string.IsNullOrWhiteSpace(plainText))
        {
            return plainText;
        }

        var htmlText = FindBodyByMimeType(payload, "text/html");
        if (string.IsNullOrWhiteSpace(htmlText))
        {
            return null;
        }

        return StripHtml(htmlText);
    }

    private static string? FindBodyByMimeType(JsonElement part, string mimeType)
    {
        if (part.TryGetProperty("mimeType", out var mimeTypeElement)
            && string.Equals(mimeTypeElement.GetString(), mimeType, StringComparison.OrdinalIgnoreCase)
            && TryDecodeBody(part, out var bodyText))
        {
            return bodyText;
        }

        if (!part.TryGetProperty("parts", out var partsElement) || partsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var childPart in partsElement.EnumerateArray())
        {
            var childResult = FindBodyByMimeType(childPart, mimeType);
            if (!string.IsNullOrWhiteSpace(childResult))
            {
                return childResult;
            }
        }

        return null;
    }

    private static bool TryDecodeBody(JsonElement part, out string? bodyText)
    {
        bodyText = null;

        if (!part.TryGetProperty("body", out var bodyElement)
            || !bodyElement.TryGetProperty("data", out var dataElement)
            || dataElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var data = dataElement.GetString();
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var normalized = data.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
        }

        try
        {
            var bytes = Convert.FromBase64String(normalized);
            bodyText = Encoding.UTF8.GetString(bytes);
            return !string.IsNullOrWhiteSpace(bodyText);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string StripHtml(string html)
    {
        var withoutBreaks = Regex.Replace(html, "<(br|BR)\\s*/?>", Environment.NewLine, RegexOptions.Compiled);
        var withoutParagraphs = Regex.Replace(withoutBreaks, "</p>", Environment.NewLine + Environment.NewLine, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var withoutTags = Regex.Replace(withoutParagraphs, "<.*?>", string.Empty, RegexOptions.Singleline | RegexOptions.Compiled);
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
