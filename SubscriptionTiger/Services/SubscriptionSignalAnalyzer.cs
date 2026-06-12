using System.Globalization;
using System.Text.RegularExpressions;
using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class SubscriptionSignalAnalyzer
{
    private const int MonthlyMinDays = 25;
    private const int MonthlyMaxDays = 35;
    private const int AnnualMinDays = 300;
    private const int AnnualMaxDays = 460;

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

    // Known senders for a subset of brands. Used to tell a real vendor email apart from a message that
    // merely mentions the brand in its text. Only obvious, currently relevant domains are listed.
    private static readonly Dictionary<string, string[]> TrustedVendorDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft"] = ["microsoft.com", "live.com", "outlook.com", "office.com", "xbox.com"],
        ["Netflix"] = ["netflix.com"],
        ["McAfee"] = ["mcafee.com"]
    };

    /// <summary>
    /// Returns the canonical trusted domain for a known vendor from the curated allow-list, if one
    /// exists. Used by UI helpers (e.g. Cancel Help) to offer a safe "open official website" action
    /// without ever trusting a raw sender domain or a link parsed from an email body.
    /// </summary>
    public static bool TryGetTrustedVendorDomain(string vendor, out string domain)
    {
        if (!string.IsNullOrWhiteSpace(vendor)
            && TrustedVendorDomains.TryGetValue(vendor, out var domains)
            && domains.Length > 0)
        {
            domain = domains[0];
            return true;
        }

        domain = string.Empty;
        return false;
    }

    public bool TryAnalyze(
        string sender,
        string subject,
        string previewText,
        out int confidenceScore,
        out string reason,
        out decimal? price,
        out BillingCycle billingCycle,
        out string vendor)
    {
        var analysis = AnalyzeMessage(subject, previewText, sender);
        if (!analysis.IsMatch)
        {
            confidenceScore = 0;
            reason = string.Empty;
            price = null;
            billingCycle = BillingCycle.Unknown;
            vendor = string.Empty;
            return false;
        }

        confidenceScore = analysis.ConfidenceScore;
        reason = analysis.Reason;
        price = analysis.Price;
        billingCycle = analysis.BillingCycle;
        vendor = DeriveVendorName(sender, subject, previewText);
        return true;
    }

    public IReadOnlyList<SubscriptionCandidate> ApplyRecurringEvidence(IEnumerable<SubscriptionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            return candidateList;
        }

        var updatedById = candidateList.ToDictionary(x => x.Id, x => x);
        var groups = candidateList
            .GroupBy(x => NormalizeVendorKey(x.Vendor), StringComparer.Ordinal)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToList();
        var emailEvidenceGroups = candidateList
            .GroupBy(BuildEmailEvidenceKey, StringComparer.Ordinal)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToList();

        foreach (var group in groups)
        {
            var groupList = group.ToList();
            var dated = groupList
                .Where(x => x.SourceEmailDate.HasValue)
                .OrderBy(x => x.SourceEmailDate)
                .ToList();

            var monthlyIntervalCount = CountIntervals(dated, MonthlyMinDays, MonthlyMaxDays);
            var annualIntervalCount = CountIntervals(dated, AnnualMinDays, AnnualMaxDays);
            var recurringAmount = HasRecurringAmount(groupList);

            foreach (var candidate in groupList)
            {
                var updated = candidate;
                var updatedReason = candidate.DetectionReason;
                var updatedConfidence = candidate.ConfidenceScore;
                var updatedCycle = candidate.BillingCycle;

                if (monthlyIntervalCount > 0)
                {
                    updatedConfidence = Math.Min(99, updatedConfidence + (dated.Count >= 3 ? 20 : 12) + (recurringAmount ? 5 : 0));
                    if (dated.Count >= 3)
                    {
                        updatedConfidence = Math.Max(updatedConfidence, 80);
                    }

                    updatedCycle = BillingCycle.Monthly;
                    updatedReason = AppendReason(updatedReason, recurringAmount
                        ? "Recurring monthly pattern detected across similar vendor and amount history."
                        : "Recurring monthly pattern detected across similar vendor history.");
                }

                if (annualIntervalCount > 0)
                {
                    updatedConfidence = Math.Min(99, updatedConfidence + 14 + (recurringAmount ? 4 : 0));
                    if (updatedCycle == BillingCycle.Unknown)
                    {
                        updatedCycle = BillingCycle.Yearly;
                    }

                    updatedReason = AppendReason(updatedReason, "Recurring yearly pattern detected across roughly 10–15 months.");
                }

                if (groupList.Count == 1 && ContainsAny($"{candidate.SourceEmailSubject} {candidate.SourceEmailSnippet} {candidate.DetectionReason}", YearlyTerms))
                {
                    if (updatedCycle == BillingCycle.Unknown)
                    {
                        updatedCycle = BillingCycle.Yearly;
                    }

                    updatedReason = AppendReason(updatedReason, "Possible annual subscription — annual renewal wording found.");
                }

                updatedById[candidate.Id] = updated with
                {
                    ConfidenceScore = updatedConfidence,
                    BillingCycle = updatedCycle,
                    DetectionReason = updatedReason
                };
            }
        }

        foreach (var group in emailEvidenceGroups)
        {
            var groupList = group
                .OrderBy(x => x.SourceEmailDate ?? DateTimeOffset.MinValue)
                .ToList();

            if (groupList.Count < 2)
            {
                continue;
            }

            var monthlyIntervalCount = CountIntervals(groupList, MonthlyMinDays, MonthlyMaxDays);
            var annualIntervalCount = CountIntervals(groupList, 330, 400);
            var sharedEvidenceReason = BuildSharedEvidenceReason(groupList.Count, monthlyIntervalCount, annualIntervalCount);

            foreach (var candidate in groupList)
            {
                if (!updatedById.TryGetValue(candidate.Id, out var updated))
                {
                    continue;
                }

                var updatedConfidence = updated.ConfidenceScore;
                var updatedCycle = updated.BillingCycle;
                var updatedReason = updated.DetectionReason;

                if (groupList.Count >= 3)
                {
                    updatedConfidence = Math.Max(updatedConfidence, 68);
                }
                else
                {
                    updatedConfidence = Math.Max(updatedConfidence, 48);
                }

                if (monthlyIntervalCount > 0 && groupList.Count >= 3)
                {
                    updatedCycle = BillingCycle.Monthly;
                    updatedConfidence = Math.Max(updatedConfidence, 82);
                }
                else if (annualIntervalCount > 0 && groupList.Count >= 2 && updatedCycle == BillingCycle.Unknown)
                {
                    updatedCycle = BillingCycle.Yearly;
                    updatedConfidence = Math.Max(updatedConfidence, 58);
                }

                updatedById[candidate.Id] = updated with
                {
                    ConfidenceScore = updatedConfidence,
                    BillingCycle = updatedCycle,
                    DetectionReason = AppendReason(updatedReason, sharedEvidenceReason)
                };
            }
        }

        return candidateList.Select(x => updatedById[x.Id]).ToList();
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

        var senderDomain = NormalizeSenderDomain(sender);
        var senderMatches = GetMatchedVendors(senderText);
        var strongVendorMatches = senderMatches
            .Where(v => !IsConstrainedVendor(v) || IsStrongVendorMatch(v, senderDomain))
            .ToList();
        var hasVendorSignal = strongVendorMatches.Count > 0;
        var mismatchedBrand = GetMatchedVendors(combinedText)
            .FirstOrDefault(v => IsConstrainedVendor(v) && !IsStrongVendorMatch(v, senderDomain));

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
            reasonParts.Add($"sender matched {strongVendorMatches[0]}");
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

        if (mismatchedBrand is not null)
        {
            var displayDomain = string.IsNullOrWhiteSpace(senderDomain) ? "unknown" : senderDomain;
            reasonParts.Insert(0, $"Brand mention only: {mismatchedBrand} mentioned, sender domain {displayDomain} not trusted for {mismatchedBrand}");
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

        if (mismatchedBrand is not null && confidenceBand == ConfidenceBand.High)
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

    private static int CountIntervals(IReadOnlyList<SubscriptionCandidate> datedCandidates, int minDays, int maxDays)
    {
        if (datedCandidates.Count < 2)
        {
            return 0;
        }

        var count = 0;
        for (var i = 1; i < datedCandidates.Count; i++)
        {
            var previousDate = datedCandidates[i - 1].SourceEmailDate;
            var currentDate = datedCandidates[i].SourceEmailDate;
            if (!previousDate.HasValue || !currentDate.HasValue)
            {
                continue;
            }

            var daysApart = Math.Abs((currentDate.Value - previousDate.Value).TotalDays);
            if (daysApart >= minDays && daysApart <= maxDays)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasRecurringAmount(IReadOnlyList<SubscriptionCandidate> candidates)
    {
        var prices = candidates.Where(x => x.Price.HasValue).Select(x => x.Price!.Value).ToList();
        if (prices.Count < 2)
        {
            return false;
        }

        var normalized = prices.OrderBy(x => x).ToList();
        for (var i = 1; i < normalized.Count; i++)
        {
            var left = normalized[i - 1];
            var right = normalized[i];
            var tolerance = Math.Max(2m, Math.Min(left, right) * 0.10m);
            if (Math.Abs(right - left) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeVendorKey(string? vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(vendor.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
        return normalized;
    }

    private static string BuildEmailEvidenceKey(SubscriptionCandidate candidate)
    {
        var domain = NormalizeSenderDomain(candidate.SourceEmailSender);
        var normalizedSubject = NormalizeEmailSubject(candidate.SourceEmailSubject);
        var source = candidate.Source.ToString();

        if (string.IsNullOrWhiteSpace(domain) && string.IsNullOrWhiteSpace(normalizedSubject))
        {
            return string.Empty;
        }

        return $"{source}|{domain}|{normalizedSubject}";
    }

    private static string NormalizeSenderDomain(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
        {
            return string.Empty;
        }

        var match = Regex.Match(sender, @"@([A-Za-z0-9.-]+)");
        if (match.Success)
        {
            return match.Groups[1].Value.ToLowerInvariant();
        }

        return NormalizeEmailSubject(sender);
    }

    private static string NormalizeEmailSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        var normalized = subject.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\b(re|fw|fwd):\s*", string.Empty);
        normalized = Regex.Replace(normalized, @"\b\d{1,4}[/-]\d{1,2}([/-]\d{1,4})?\b", " ");
        normalized = Regex.Replace(normalized, @"\b(invoice|receipt|payment|charged|billing|renewal|subscription|plan|membership)\b", "$1");
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", string.Empty);
        return normalized;
    }

    private static string BuildSharedEvidenceReason(int emailCount, int monthlyIntervalCount, int annualIntervalCount)
    {
        var level = emailCount >= 3 ? "suspected" : "possible";
        var reason = $"Shared email evidence: {emailCount} similar billing emails ({level}).";

        if (monthlyIntervalCount > 0 && emailCount >= 3)
        {
            return $"{reason} Spacing suggests likely monthly billing (25–35 day gaps).";
        }

        if (annualIntervalCount > 0 && emailCount >= 2)
        {
            return $"{reason} Spacing suggests possible yearly billing (330–400 day gaps).";
        }

        return reason;
    }

    private static string AppendReason(string existingReason, string additionalReason)
    {
        if (string.IsNullOrWhiteSpace(existingReason))
        {
            return additionalReason;
        }

        if (existingReason.Contains(additionalReason, StringComparison.OrdinalIgnoreCase))
        {
            return existingReason;
        }

        return $"{existingReason}; {additionalReason}";
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

    private static bool IsConstrainedVendor(string vendor)
        => TrustedVendorDomains.ContainsKey(vendor);

    // A known brand is "strong" only when the sender domain is trusted for it (or the domain itself
    // carries the brand). A brand appearing solely in the display name or body is not a strong match.
    private static bool IsStrongVendorMatch(string vendor, string senderDomain)
    {
        if (IsDomainTrustedForVendor(vendor, senderDomain))
        {
            return true;
        }

        var clue = GetClueForVendor(vendor);
        return !string.IsNullOrEmpty(clue)
            && !string.IsNullOrWhiteSpace(senderDomain)
            && senderDomain.Contains(clue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDomainTrustedForVendor(string vendor, string senderDomain)
    {
        if (string.IsNullOrWhiteSpace(senderDomain)
            || !TrustedVendorDomains.TryGetValue(vendor, out var trustedDomains))
        {
            return false;
        }

        foreach (var trusted in trustedDomains)
        {
            if (string.Equals(senderDomain, trusted, StringComparison.OrdinalIgnoreCase)
                || senderDomain.EndsWith("." + trusted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetClueForVendor(string vendor)
    {
        foreach (var (clue, candidateVendor) in VendorClues)
        {
            if (string.Equals(candidateVendor, vendor, StringComparison.OrdinalIgnoreCase))
            {
                return clue;
            }
        }

        return string.Empty;
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
        var senderDomain = NormalizeSenderDomain(sender);
        var knownVendors = GetMatchedVendors($"{sender} {subject} {snippet}");

        // Prefer a brand that the sender domain actually corroborates (or an unconstrained brand).
        var strongVendor = knownVendors
            .FirstOrDefault(v => !IsConstrainedVendor(v) || IsStrongVendorMatch(v, senderDomain));
        if (strongVendor is not null)
        {
            return strongVendor;
        }

        // Remaining matches are constrained brands on untrusted domains (text mentions only).
        // Fall through to the sender-derived identity instead of trusting the brand text.
        var weakBrandClues = knownVendors
            .Where(v => IsConstrainedVendor(v) && !IsStrongVendorMatch(v, senderDomain))
            .Select(GetClueForVendor)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        var senderName = sender.Split('<')[0].Trim('"', ' ', '\t');
        var senderNameEchoesBrand = weakBrandClues.Any(c => senderName.Contains(c, StringComparison.OrdinalIgnoreCase));

        if (!senderNameEchoesBrand
            && !string.IsNullOrWhiteSpace(senderName)
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
