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
    string? SourceEmailSnippet = null,
    string? SourceMessageId = null,
    string? SourceThreadId = null,
    int OccurrenceCount = 1,
    DateTime? FirstSeenDate = null,
    DateTime? LastSeenDate = null,
    DateTime? LastSourceEmailDate = null,
    string? RecurringEvidenceSummary = null)
{
    /// <summary>
    /// Provider message ids already counted toward <see cref="OccurrenceCount"/>. Tracked so that
    /// re-scanning the same mailbox does not count the exact same email again and inflate evidence.
    /// </summary>
    public List<string> SeenSourceMessageIds { get; init; } = new();
}
