namespace SubscriptionTiger.Models;

public sealed record ConfirmedSubscription(
    Guid Id,
    string Vendor,
    decimal? Price,
    BillingCycle BillingCycle,
    DateTime RenewalDate,
    SubscriptionStatus Status,
    SubscriptionSource Source);
