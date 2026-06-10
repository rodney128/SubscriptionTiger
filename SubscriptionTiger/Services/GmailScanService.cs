using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class GmailScanService : IGmailScanService
{
    private const string GmailSearchQuery = "(subscription OR subscribed OR renewal OR renews OR billing OR invoice OR receipt OR payment OR paid OR charged OR plan OR membership OR trial OR \"free trial\" OR cancel OR \"manage subscription\" OR \"upcoming payment\" OR monthly OR yearly OR annual)";

    private static readonly string[] PositiveTerms =
    [
        "subscription",
        "subscribed",
        "renewal",
        "renews",
        "billing",
        "receipt",
        "invoice",
        "payment",
        "paid",
        "charged",
        "plan",
        "membership",
        "trial",
        "free trial",
        "cancel",
        "manage subscription",
        "upcoming payment",
        "monthly",
        "yearly",
        "annual"
    ];

    private static readonly string[] SubscriptionTerms =
    [
        "subscription",
        "subscribed",
        "renewal",
        "renews",
        "membership",
        "trial",
        "free trial",
        "manage subscription",
        "plan"
    ];

    private static readonly string[] PaymentTerms =
    [
        "billing",
        "invoice",
        "receipt",
        "payment",
        "paid",
        "charged",
        "upcoming payment"
    ];

    private static readonly string[] MonthlyTerms =
    [
        "monthly",
        "per month",
        "/month",
        "every month",
        "month-to-month"
    ];

    private static readonly string[] YearlyTerms =
    [
        "yearly",
        "annual",
        "annually",
        "per year",
        "/year",
        "renews yearly",
        "renews annually"
    ];

    private static readonly string[] ExclusionTerms =
    [
        "password reset",
        "security alert",
        "verify your account",
        "sign-in attempt",
        "one-time passcode",
        "otp",
        "shipping",
        "out for delivery",
        "tracking number",
        "delivered",
        "newsletter",
        "marketing"
    ];

    private static readonly (string Clue, string Vendor)[] VendorClues =
    [
        ("netflix", "Netflix"),
        ("disney", "Disney"),
        ("spotify", "Spotify"),
        ("amazon", "Amazon"),
        ("youtube", "YouTube"),
        ("google", "Google"),
        ("apple", "Apple"),
        ("microsoft", "Microsoft"),
        ("adobe", "Adobe"),
        ("dropbox", "Dropbox"),
        ("icloud", "iCloud"),
        ("patreon", "Patreon"),
        ("openai", "OpenAI"),
        ("chatgpt", "ChatGPT"),
        ("canva", "Canva"),
        ("grammarly", "Grammarly"),
        ("norton", "Norton"),
        ("mcafee", "McAfee"),
        ("godaddy", "GoDaddy"),
        ("namecheap", "Namecheap"),
        ("hostinger", "Hostinger"),
        ("bluehost", "Bluehost")
    ];

    private readonly IGmailAuthService authService;
    private readonly HttpClient httpClient;
    private readonly DiagnosticsService diagnosticsService;

    public GmailScanService(IGmailAuthService authService, HttpClient httpClient, DiagnosticsService diagnosticsService)
    {
        this.authService = authService;
        this.httpClient = httpClient;
        this.diagnosticsService = diagnosticsService;
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

        var analysis = AnalyzeMessage(subject, snippet, sender);
        if (!analysis.IsMatch)
        {
            return null;
        }

        var sourceDate = TryParseDate(dateRaw);
        var vendor = DeriveVendorName(sender, subject, snippet);

        return new SubscriptionCandidate(
            Guid.NewGuid(),
            vendor,
            Price: analysis.Price,
            analysis.BillingCycle,
            analysis.ConfidenceScore,
            SubscriptionSource.Gmail,
            analysis.Reason,
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

    private static DetectionAnalysis AnalyzeMessage(string subject, string snippet, string sender)
    {
        var subjectText = subject.ToLowerInvariant();
        var snippetText = snippet.ToLowerInvariant();
        var senderText = sender.ToLowerInvariant();
        var combinedText = $"{subjectText} {snippetText} {senderText}";

        var subjectSubscriptionMatches = GetMatchedTerms(subjectText, SubscriptionTerms);
        var snippetSubscriptionMatches = GetMatchedTerms(snippetText, SubscriptionTerms);
        var subjectPaymentMatches = GetMatchedTerms(subjectText, PaymentTerms);
        var snippetPaymentMatches = GetMatchedTerms(snippetText, PaymentTerms);

        var hasSubscriptionSignal = subjectSubscriptionMatches.Count > 0 || snippetSubscriptionMatches.Count > 0;
        var hasPaymentSignal = subjectPaymentMatches.Count > 0 || snippetPaymentMatches.Count > 0;

        if (!hasSubscriptionSignal && !hasPaymentSignal)
        {
            return DetectionAnalysis.NoMatch;
        }

        if (ContainsAny(combinedText, ExclusionTerms) && !hasPaymentSignal)
        {
            return DetectionAnalysis.NoMatch;
        }

        var score = 0;
        var reasonParts = new List<string>();

        var senderMatches = GetMatchedVendors(senderText);
        var hasVendorSignal = senderMatches.Count > 0;

        var subjectMatches = GetMatchedTerms(subjectText, PositiveTerms);
        if (subjectMatches.Count > 0)
        {
            score += Math.Min(42, subjectMatches.Count * 14);
            reasonParts.Add($"subject contained {string.Join(", ", subjectMatches.Take(2))}");
        }

        var snippetMatches = GetMatchedTerms(snippetText, PositiveTerms);
        if (snippetMatches.Count > 0)
        {
            score += Math.Min(30, snippetMatches.Count * 10);
            reasonParts.Add($"snippet contained {string.Join(", ", snippetMatches.Take(2))}");
        }

        if (hasVendorSignal)
        {
            score += 15;
            reasonParts.Add($"sender matched {senderMatches[0]}");
        }

        var billingCycle = DetectBillingCycle(combinedText, out var cycleDetected);
        if (cycleDetected)
        {
            score += 8;
            reasonParts.Add($"cycle looked {billingCycle.ToString().ToLowerInvariant()}");
        }

        var price = TryExtractPrice(subject, snippet);
        if (price.HasValue)
        {
            score += 12;
            reasonParts.Add("price was visible");
        }

        if (ContainsAny(combinedText, ["newsletter", "sale", "promo", "promotion", "deal"]) && !hasPaymentSignal)
        {
            score -= 20;
        }

        if (ContainsAny(combinedText, ["password reset", "security alert", "sign-in attempt", "tracking number", "out for delivery"]))
        {
            score -= 30;
        }

        var totalMeaningfulSignals = CountSignals(
            hasVendorSignal,
            hasSubscriptionSignal,
            hasPaymentSignal,
            cycleDetected,
            price.HasValue);

        if (score < 40)
        {
            return DetectionAnalysis.NoMatch;
        }

        var confidenceBand = ToConfidenceBand(score, totalMeaningfulSignals);
        if (confidenceBand == ConfidenceBand.Low && !(hasVendorSignal && (hasSubscriptionSignal || hasPaymentSignal)))
        {
            return DetectionAnalysis.NoMatch;
        }

        if (confidenceBand == ConfidenceBand.Low)
        {
            reasonParts.Insert(0, "Low confidence — review manually");
            if (!price.HasValue && billingCycle == BillingCycle.Unknown)
            {
                reasonParts.Add("price and cycle were not visible");
            }
        }

        if (confidenceBand == ConfidenceBand.Medium && totalMeaningfulSignals < 2)
        {
            return DetectionAnalysis.NoMatch;
        }

        if (confidenceBand == ConfidenceBand.High && !(hasPaymentSignal && (price.HasValue || cycleDetected || hasVendorSignal)))
        {
            confidenceBand = ConfidenceBand.Medium;
        }

        var reason = reasonParts.Count == 0
            ? "matched subscription and billing clues"
            : string.Join("; ", reasonParts.Take(3));

        var finalScore = confidenceBand switch
        {
            ConfidenceBand.High => Math.Clamp(Math.Max(score, 80), 1, 99),
            ConfidenceBand.Medium => Math.Clamp(Math.Max(Math.Min(score, 79), 55), 1, 99),
            _ => Math.Clamp(Math.Min(score, 54), 1, 99)
        };

        return new DetectionAnalysis(
            IsMatch: true,
            ConfidenceScore: finalScore,
            Reason: reason,
            Price: price,
            BillingCycle: billingCycle);
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> GetMatchedTerms(string text, IEnumerable<string> terms)
    {
        var matched = new List<string>();
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(term);
            }
        }

        return matched.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

    private static List<string> GetMatchedVendors(string text)
    {
        var vendors = new List<string>();
        foreach (var (clue, vendor) in VendorClues)
        {
            if (text.Contains(clue, StringComparison.OrdinalIgnoreCase))
            {
                vendors.Add(vendor);
            }
        }

        return vendors;
    }

    private static BillingCycle DetectBillingCycle(string combinedText, out bool detected)
    {
        if (ContainsAny(combinedText, YearlyTerms))
        {
            detected = true;
            return BillingCycle.Yearly;
        }

        if (ContainsAny(combinedText, MonthlyTerms))
        {
            detected = true;
            return BillingCycle.Monthly;
        }

        detected = false;
        return BillingCycle.Unknown;
    }

    private static decimal? TryExtractPrice(string subject, string snippet)
    {
        var text = $"{subject} {snippet}";
        var match = Regex.Match(text, @"(?:\$|USD\s?)(\d{1,4}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var rawAmount = match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return amount > 0 ? amount : null;
    }

    private static string DeriveVendorName(string sender, string subject, string snippet)
    {
        var knownVendors = GetMatchedVendors($"{sender} {subject} {snippet}");
        if (knownVendors.Count > 0)
        {
            return knownVendors[0];
        }

        var senderName = sender.Split('<')[0].Trim('"', ' ', '\t');
        if (!string.IsNullOrWhiteSpace(senderName)
            && !senderName.Contains('@', StringComparison.Ordinal)
            && senderName.Length <= 80)
        {
            return senderName;
        }

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

    private sealed record DetectionAnalysis(
        bool IsMatch,
        int ConfidenceScore,
        string Reason,
        decimal? Price,
        BillingCycle BillingCycle)
    {
        public static DetectionAnalysis NoMatch { get; } = new(
            IsMatch: false,
            ConfidenceScore: 0,
            Reason: string.Empty,
            Price: null,
            BillingCycle: BillingCycle.Unknown);
    }

    private static int CountSignals(bool hasVendorSignal, bool hasSubscriptionSignal, bool hasPaymentSignal, bool cycleDetected, bool hasPrice)
    {
        var count = 0;
        if (hasVendorSignal)
        {
            count++;
        }

        if (hasSubscriptionSignal)
        {
            count++;
        }

        if (hasPaymentSignal)
        {
            count++;
        }

        if (cycleDetected)
        {
            count++;
        }

        if (hasPrice)
        {
            count++;
        }

        return count;
    }

    private static ConfidenceBand ToConfidenceBand(int score, int signalCount)
    {
        if (score >= 78 && signalCount >= 3)
        {
            return ConfidenceBand.High;
        }

        if (score >= 55 && signalCount >= 2)
        {
            return ConfidenceBand.Medium;
        }

        return ConfidenceBand.Low;
    }

    private enum ConfidenceBand
    {
        Low,
        Medium,
        High
    }
}
