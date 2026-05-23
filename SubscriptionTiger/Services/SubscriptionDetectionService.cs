using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class SubscriptionDetectionService
{
    public IReadOnlyList<SubscriptionCandidate> Scan(SubscriptionSource source)
    {
        return source switch
        {
            SubscriptionSource.Gmail => CreateGmailSamples(),
            SubscriptionSource.Outlook => CreateOutlookSamples(),
            SubscriptionSource.Manual => Array.Empty<SubscriptionCandidate>(),
            _ => Array.Empty<SubscriptionCandidate>()
        };
    }

    private static IReadOnlyList<SubscriptionCandidate> CreateGmailSamples()
    {
        return
        [
            new SubscriptionCandidate(Guid.NewGuid(), "Netflix", 15.49m, BillingCycle.Monthly, 94, SubscriptionSource.Gmail, "Recurring charge and renewal email pattern detected"),
            new SubscriptionCandidate(Guid.NewGuid(), "Spotify", 10.99m, BillingCycle.Monthly, 91, SubscriptionSource.Gmail, "Subscription receipt cadence matched monthly cycle"),
            new SubscriptionCandidate(Guid.NewGuid(), "Amazon Prime", 139.00m, BillingCycle.Yearly, 89, SubscriptionSource.Gmail, "Annual membership renewal notice identified")
        ];
    }

    private static IReadOnlyList<SubscriptionCandidate> CreateOutlookSamples()
    {
        return
        [
            new SubscriptionCandidate(Guid.NewGuid(), "Microsoft 365", 99.99m, BillingCycle.Yearly, 93, SubscriptionSource.Outlook, "Recurring invoice and auto-renew references found"),
            new SubscriptionCandidate(Guid.NewGuid(), "Adobe", 59.99m, BillingCycle.Monthly, 88, SubscriptionSource.Outlook, "Billing statement cadence indicates monthly subscription")
        ];
    }
}
