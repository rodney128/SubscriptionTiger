namespace SubscriptionTiger.Models;

public sealed record SubscriptionCandidate(
    Guid Id,
    string Vendor,
    decimal? Price,
    BillingCycle BillingCycle,
    int ConfidenceScore,
    SubscriptionSource Source,
    string DetectionReason,
    string? SourceEmailSubject = null,
    string? SourceEmailSender = null,
    DateTimeOffset? SourceEmailDate = null,
    string? SourceEmailSnippet = null);
