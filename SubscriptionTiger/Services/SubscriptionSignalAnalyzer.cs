using System.Globalization;
using System.Text.RegularExpressions;
using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class SubscriptionSignalAnalyzer
{
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
