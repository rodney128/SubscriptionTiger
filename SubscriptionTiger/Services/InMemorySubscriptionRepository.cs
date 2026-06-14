using System.Globalization;
using System.Text.RegularExpressions;
using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class InMemorySubscriptionRepository
{
    private const string SampleVendor = "Gym Membership";
    private const decimal SamplePrice = 45.99m;
    private const BillingCycle SampleBillingCycle = BillingCycle.Monthly;

    private readonly List<SubscriptionCandidate> suspectedCandidates = new();
    private readonly List<ConfirmedSubscription> confirmedSubscriptions = new();
    private readonly HashSet<string> ignoredSignatures = new(StringComparer.Ordinal);

    public IReadOnlyList<SubscriptionCandidate> SuspectedCandidates => suspectedCandidates;

    public IReadOnlyList<ConfirmedSubscription> ConfirmedSubscriptions => confirmedSubscriptions;

    /// <summary>
    /// Signatures of suspects the user marked as "not a subscription". Persisted by the caller so the
    /// same suspect does not reappear after an app restart or a later scan.
    /// </summary>
    public IReadOnlyCollection<string> IgnoredSignatures => ignoredSignatures;

    public void SetConfirmedSubscriptions(IEnumerable<ConfirmedSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        confirmedSubscriptions.Clear();
        confirmedSubscriptions.AddRange(subscriptions);
    }

    /// <summary>
    /// Replaces the ignored-suspect signatures, typically from local storage at startup.
    /// </summary>
    public void SetIgnoredSignatures(IEnumerable<string> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);

        ignoredSignatures.Clear();
        foreach (var signature in signatures)
        {
            if (!string.IsNullOrWhiteSpace(signature))
            {
                ignoredSignatures.Add(signature);
            }
        }
    }

    public CandidateAddResult AddCandidates(IEnumerable<SubscriptionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var candidate in candidates)
        {
            if (IsIgnored(candidate))
            {
                duplicateCount++;
                continue;
            }

            var alreadyConfirmed = confirmedSubscriptions.Any(x =>
                string.Equals(x.Vendor, candidate.Vendor, StringComparison.OrdinalIgnoreCase)
                && x.BillingCycle == candidate.BillingCycle);

            if (alreadyConfirmed)
            {
                duplicateCount++;
                continue;
            }

            var existingIndex = suspectedCandidates.FindIndex(x => IsRecurrenceMatch(x, candidate));
            if (existingIndex >= 0)
            {
                suspectedCandidates[existingIndex] = MergeEvidence(suspectedCandidates[existingIndex], candidate);
                duplicateCount++;
                continue;
            }

            suspectedCandidates.Add(InitializeEvidence(candidate));
            addedCount++;
        }

        return new CandidateAddResult(addedCount, duplicateCount);
    }

    public ConfirmedSubscription? SaveCandidate(Guid candidateId)
    {
        var candidate = suspectedCandidates.FirstOrDefault(x => x.Id == candidateId);
        if (candidate is null)
        {
            return null;
        }

        suspectedCandidates.Remove(candidate);

        var renewalDate = candidate.BillingCycle switch
        {
            BillingCycle.Monthly => DateTime.Today.AddMonths(1),
            BillingCycle.Yearly => DateTime.Today.AddMonths(12),
            _ => DateTime.Today
        };
        var confirmed = new ConfirmedSubscription(
            candidate.Id,
            candidate.Vendor,
            candidate.Price,
            candidate.BillingCycle,
            renewalDate,
            SubscriptionStatus.Active,
            candidate.Source);

        confirmedSubscriptions.Add(confirmed);
        return confirmed;
    }

    public ConfirmedSubscription AddManualSubscription(
        string vendor,
        decimal price,
        BillingCycle billingCycle,
        DateTime renewalDate)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            throw new ArgumentException("Vendor is required.", nameof(vendor));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");
        }

        var normalizedVendor = vendor.Trim();
        var duplicateExists = confirmedSubscriptions.Any(x =>
            x.BillingCycle == billingCycle
            && string.Equals(x.Vendor, normalizedVendor, StringComparison.OrdinalIgnoreCase));
        if (duplicateExists)
        {
            throw new InvalidOperationException("A subscription with the same vendor and billing cycle already exists.");
        }

        var confirmed = new ConfirmedSubscription(
            Guid.NewGuid(),
            normalizedVendor,
            price,
            billingCycle,
            renewalDate,
            SubscriptionStatus.Active,
            SubscriptionSource.Manual);

        confirmedSubscriptions.Add(confirmed);
        return confirmed;
    }

    public bool DeleteConfirmedSubscription(Guid confirmedSubscriptionId)
    {
        var subscription = confirmedSubscriptions.FirstOrDefault(x => x.Id == confirmedSubscriptionId);
        if (subscription is null)
        {
            return false;
        }

        return confirmedSubscriptions.Remove(subscription);
    }

    public bool DismissCandidate(Guid candidateId)
    {
        var candidate = suspectedCandidates.FirstOrDefault(x => x.Id == candidateId);
        if (candidate is null)
        {
            return false;
        }

        return suspectedCandidates.Remove(candidate);
    }

    /// <summary>
    /// Marks a suspected candidate as "not a subscription": removes it (and any suspected entries that
    /// share the same ignore signature) and records the signature so future scans skip it. Returns the
    /// recorded signature, or null when the candidate no longer exists.
    /// </summary>
    public string? IgnoreCandidate(Guid candidateId)
    {
        var candidate = suspectedCandidates.FirstOrDefault(x => x.Id == candidateId);
        if (candidate is null)
        {
            return null;
        }

        var signature = SubscriptionSignalAnalyzer.BuildIgnoreSignature(candidate);
        ignoredSignatures.Add(signature);
        suspectedCandidates.RemoveAll(x =>
            string.Equals(SubscriptionSignalAnalyzer.BuildIgnoreSignature(x), signature, StringComparison.Ordinal));
        return signature;
    }

    private bool IsIgnored(SubscriptionCandidate candidate)
        => ignoredSignatures.Count > 0
            && ignoredSignatures.Contains(SubscriptionSignalAnalyzer.BuildIgnoreSignature(candidate));

    public bool AddManualSampleIfMissing()
    {
        if (SampleAlreadyExists())
        {
            return false;
        }

        var manual = new ConfirmedSubscription(
            Guid.NewGuid(),
            SampleVendor,
            SamplePrice,
            SampleBillingCycle,
            DateTime.Today.AddMonths(1),
            SubscriptionStatus.Active,
            SubscriptionSource.Manual);

        confirmedSubscriptions.Add(manual);
        return true;
    }

    public void RemoveDuplicateSampleSubscriptions()
    {
        var uniqueConfirmed = new List<ConfirmedSubscription>(confirmedSubscriptions.Count);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subscription in confirmedSubscriptions)
        {
            var key = $"{subscription.Vendor.Trim()}|{subscription.Price?.ToString() ?? string.Empty}|{subscription.BillingCycle}";
            if (keys.Add(key))
            {
                uniqueConfirmed.Add(subscription);
            }
        }

        if (uniqueConfirmed.Count == confirmedSubscriptions.Count)
        {
            return;
        }

        confirmedSubscriptions.Clear();
        confirmedSubscriptions.AddRange(uniqueConfirmed);
    }

    public bool ClearAllTestData()
    {
        var hadData = suspectedCandidates.Count > 0 || confirmedSubscriptions.Count > 0;

        suspectedCandidates.Clear();
        confirmedSubscriptions.Clear();
        ignoredSignatures.Clear();

        return hadData;
    }

    private bool SampleAlreadyExists()
    {
        var sampleExistsInConfirmed = confirmedSubscriptions.Any(x =>
            string.Equals(x.Vendor, SampleVendor, StringComparison.OrdinalIgnoreCase)
            && x.BillingCycle == SampleBillingCycle
            && x.Price == SamplePrice);

        if (sampleExistsInConfirmed)
        {
            return true;
        }

        var sampleExistsInSuspected = suspectedCandidates.Any(x =>
            string.Equals(x.Vendor, SampleVendor, StringComparison.OrdinalIgnoreCase)
            && x.BillingCycle == SampleBillingCycle
            && x.Price == SamplePrice);

        return sampleExistsInSuspected;
    }

    private static bool IsRecurrenceMatch(SubscriptionCandidate existing, SubscriptionCandidate incoming)
    {
        if (existing.Source != incoming.Source)
        {
            return false;
        }

        if (!string.Equals(NormalizeVendor(existing.Vendor), NormalizeVendor(incoming.Vendor), StringComparison.Ordinal))
        {
            return false;
        }

        return existing.BillingCycle == incoming.BillingCycle
            || existing.BillingCycle == BillingCycle.Unknown
            || incoming.BillingCycle == BillingCycle.Unknown;
    }

    private static SubscriptionCandidate InitializeEvidence(SubscriptionCandidate candidate)
    {
        var now = DateTime.Now;
        var seenIds = new List<string>(candidate.SeenSourceMessageIds);
        if (!string.IsNullOrWhiteSpace(candidate.SourceMessageId)
            && !seenIds.Contains(candidate.SourceMessageId))
        {
            seenIds.Add(candidate.SourceMessageId);
        }

        return candidate with
        {
            OccurrenceCount = Math.Max(1, candidate.OccurrenceCount),
            FirstSeenDate = candidate.FirstSeenDate ?? now,
            LastSeenDate = candidate.LastSeenDate ?? now,
            LastSourceEmailDate = candidate.LastSourceEmailDate ?? candidate.SourceEmailDate?.LocalDateTime,
            SeenSourceMessageIds = seenIds
        };
    }

    private static SubscriptionCandidate MergeEvidence(SubscriptionCandidate existing, SubscriptionCandidate incoming)
    {
        var incomingMessageId = incoming.SourceMessageId;
        var hasReliableId = !string.IsNullOrWhiteSpace(incomingMessageId);

        // Re-scanning the same mailbox can surface the exact same email again. If we have already
        // counted this provider message id, treat it as a no-op so OccurrenceCount/confidence are
        // not inflated. The caller still records it as a duplicate in CandidateAddResult.
        if (hasReliableId && existing.SeenSourceMessageIds.Contains(incomingMessageId!))
        {
            return existing;
        }

        var occurrenceCount = Math.Max(1, existing.OccurrenceCount) + 1;
        var now = DateTime.Now;

        var seenIds = existing.SeenSourceMessageIds;
        if (hasReliableId)
        {
            seenIds = new List<string>(existing.SeenSourceMessageIds) { incomingMessageId! };
        }

        var incomingEmailDate = incoming.SourceEmailDate?.LocalDateTime;
        var lastSourceEmailDate = MaxDate(existing.LastSourceEmailDate, incomingEmailDate);
        var firstSeenDate = existing.FirstSeenDate ?? now;

        var bestPrice = existing.Price ?? incoming.Price;
        var bestCycle = existing.BillingCycle != BillingCycle.Unknown
            ? existing.BillingCycle
            : incoming.BillingCycle;

        var recurringAmount = AmountsSimilar(existing.Price, incoming.Price);

        var confidence = Math.Max(existing.ConfidenceScore, incoming.ConfidenceScore);
        confidence += occurrenceCount >= 3 ? 12 : 6;
        if (recurringAmount)
        {
            confidence += 4;
        }

        if (occurrenceCount >= 3)
        {
            confidence = Math.Max(confidence, 80);
        }
        else
        {
            confidence = Math.Max(confidence, 60);
        }

        confidence = Math.Clamp(confidence, 1, 99);

        var summary = BuildRecurringSummary(existing, occurrenceCount, bestCycle, bestPrice, recurringAmount);
        var detectionReason = AppendRepeatEvidence(existing.DetectionReason, summary);

        return existing with
        {
            OccurrenceCount = occurrenceCount,
            LastSeenDate = now,
            LastSourceEmailDate = lastSourceEmailDate,
            FirstSeenDate = firstSeenDate,
            Price = bestPrice,
            BillingCycle = bestCycle,
            ConfidenceScore = confidence,
            RecurringEvidenceSummary = summary,
            DetectionReason = detectionReason,
            SeenSourceMessageIds = seenIds
        };
    }

    private static string BuildRecurringSummary(
        SubscriptionCandidate existing,
        int occurrenceCount,
        BillingCycle cycle,
        decimal? price,
        bool recurringAmount)
    {
        var vendor = string.IsNullOrWhiteSpace(existing.Vendor) ? "vendor" : existing.Vendor;
        var domain = ExtractDomain(existing.SourceEmailSender);
        var sourceText = string.IsNullOrWhiteSpace(domain) ? vendor : domain;

        if (cycle == BillingCycle.Monthly)
        {
            return recurringAmount && price.HasValue
                ? $"Recurring monthly evidence: {occurrenceCount} similar emails from {sourceText}, similar amount {price.Value.ToString("C", CultureInfo.CurrentCulture)}."
                : $"Recurring monthly evidence: {occurrenceCount} similar emails from {sourceText}.";
        }

        if (cycle == BillingCycle.Yearly)
        {
            return recurringAmount && price.HasValue
                ? $"Recurring yearly evidence: {occurrenceCount} similar emails from {sourceText}, similar amount {price.Value.ToString("C", CultureInfo.CurrentCulture)}."
                : $"Recurring yearly evidence: {occurrenceCount} similar emails from {sourceText}.";
        }

        return $"Repeated evidence: {occurrenceCount} similar {vendor} billing emails found.";
    }

    private static string AppendRepeatEvidence(string existingReason, string summary)
    {
        const string marker = " | Repeat evidence: ";

        var baseReason = existingReason;
        var markerIndex = existingReason.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            baseReason = existingReason[..markerIndex];
        }

        if (string.IsNullOrWhiteSpace(baseReason))
        {
            return $"Repeat evidence: {summary}";
        }

        return $"{baseReason}{marker}{summary}";
    }

    private static bool AmountsSimilar(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return false;
        }

        var tolerance = Math.Max(2m, Math.Min(left.Value, right.Value) * 0.10m);
        return Math.Abs(left.Value - right.Value) <= tolerance;
    }

    private static DateTime? MaxDate(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static string ExtractDomain(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
        {
            return string.Empty;
        }

        var match = Regex.Match(sender, @"@([A-Za-z0-9.-]+)");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
    }

    private static string NormalizeVendor(string? vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            return string.Empty;
        }

        return Regex.Replace(vendor.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
    }
}
