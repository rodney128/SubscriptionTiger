using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class InMemorySubscriptionRepository
{
    private readonly List<SubscriptionCandidate> suspectedCandidates = new();
    private readonly List<ConfirmedSubscription> confirmedSubscriptions = new();

    public IReadOnlyList<SubscriptionCandidate> SuspectedCandidates => suspectedCandidates;

    public IReadOnlyList<ConfirmedSubscription> ConfirmedSubscriptions => confirmedSubscriptions;

    public void AddCandidates(IEnumerable<SubscriptionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        suspectedCandidates.AddRange(candidates);
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

    public bool DismissCandidate(Guid candidateId)
    {
        var candidate = suspectedCandidates.FirstOrDefault(x => x.Id == candidateId);
        if (candidate is null)
        {
            return false;
        }

        return suspectedCandidates.Remove(candidate);
    }

    public void AddManualSample()
    {
        var manual = new ConfirmedSubscription(
            Guid.NewGuid(),
            "Gym Membership",
            45.99m,
            BillingCycle.Monthly,
            DateTime.Today.AddMonths(1),
            SubscriptionStatus.Active,
            SubscriptionSource.Manual);

        confirmedSubscriptions.Add(manual);
    }
}
