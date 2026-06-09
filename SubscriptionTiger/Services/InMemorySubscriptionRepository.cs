using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class InMemorySubscriptionRepository
{
    private const string SampleVendor = "Gym Membership";
    private const decimal SamplePrice = 45.99m;
    private const BillingCycle SampleBillingCycle = BillingCycle.Monthly;

    private readonly List<SubscriptionCandidate> suspectedCandidates = new();
    private readonly List<ConfirmedSubscription> confirmedSubscriptions = new();

    public IReadOnlyList<SubscriptionCandidate> SuspectedCandidates => suspectedCandidates;

    public IReadOnlyList<ConfirmedSubscription> ConfirmedSubscriptions => confirmedSubscriptions;

    public void SetConfirmedSubscriptions(IEnumerable<ConfirmedSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        confirmedSubscriptions.Clear();
        confirmedSubscriptions.AddRange(subscriptions);
    }

    public CandidateAddResult AddCandidates(IEnumerable<SubscriptionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var candidate in candidates)
        {
            var alreadySuspected = suspectedCandidates.Any(x =>
                string.Equals(x.Vendor, candidate.Vendor, StringComparison.OrdinalIgnoreCase)
                && x.Source == candidate.Source);

            var alreadyConfirmed = confirmedSubscriptions.Any(x =>
                string.Equals(x.Vendor, candidate.Vendor, StringComparison.OrdinalIgnoreCase)
                && x.Source == candidate.Source);

            if (alreadySuspected || alreadyConfirmed)
            {
                duplicateCount++;
                continue;
            }

            suspectedCandidates.Add(candidate);
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

        var renewalDate = DateTime.Today.AddMonths(candidate.BillingCycle == BillingCycle.Monthly ? 1 : 12);
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

    public bool ClearAllTestData()
    {
        var hadData = suspectedCandidates.Count > 0 || confirmedSubscriptions.Count > 0;

        suspectedCandidates.Clear();
        confirmedSubscriptions.Clear();

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
}
