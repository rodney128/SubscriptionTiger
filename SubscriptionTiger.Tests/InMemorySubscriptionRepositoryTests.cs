using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using Xunit;

namespace SubscriptionTiger.Tests;

public class InMemorySubscriptionRepositoryTests
{
    // Case 1: a brand new suspected candidate is initialized with evidence metadata.

    [Fact]
    public void WhenNewCandidateAddedThenOccurrenceCountIsOne()
    {
        var repository = new InMemorySubscriptionRepository();

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Equal(1, stored.OccurrenceCount);
    }

    [Fact]
    public void WhenNewCandidateAddedThenSeenSourceMessageIdsContainsSourceMessageId()
    {
        var repository = new InMemorySubscriptionRepository();

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Contains("msg-1", stored.SeenSourceMessageIds);
    }

    [Fact]
    public void WhenNewCandidateAddedThenFirstSeenDateIsSet()
    {
        var repository = new InMemorySubscriptionRepository();

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.True(stored.FirstSeenDate.HasValue);
    }

    [Fact]
    public void WhenNewCandidateAddedThenLastSeenDateIsSet()
    {
        var repository = new InMemorySubscriptionRepository();

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.True(stored.LastSeenDate.HasValue);
    }

    // Case 2: the exact same provider message id seen again must not inflate evidence.

    [Fact]
    public void WhenSameMessageIdAddedAgainThenOccurrenceCountDoesNotIncrement()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Equal(1, stored.OccurrenceCount);
    }

    [Fact]
    public void WhenSameMessageIdAddedAgainThenConfidenceDoesNotIncrease()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1", confidence: 50) });
        var confidenceAfterFirstAdd = repository.SuspectedCandidates[0].ConfidenceScore;

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1", confidence: 50) });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Equal(confidenceAfterFirstAdd, stored.ConfidenceScore);
    }

    [Fact]
    public void WhenSameMessageIdAddedAgainThenSeenIdIsNotDuplicated()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Single(stored.SeenSourceMessageIds);
    }

    [Fact]
    public void WhenSameMessageIdAddedAgainThenResultReportsDuplicate()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        var result = repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        Assert.Equal(new CandidateAddResult(0, 1), result);
    }

    [Fact]
    public void WhenSameMessageIdAddedAgainThenNoNewEntryIsCreated()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        Assert.Single(repository.SuspectedCandidates);
    }

    // Case 3: a different provider message id for the same vendor is genuine new evidence.

    [Fact]
    public void WhenDifferentMessageIdAddedThenOccurrenceCountIncrements()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-2") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Equal(2, stored.OccurrenceCount);
    }

    [Fact]
    public void WhenDifferentMessageIdAddedThenNewIdIsTracked()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-2") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Contains("msg-2", stored.SeenSourceMessageIds);
    }

    [Fact]
    public void WhenDifferentMessageIdWithNewerDateAddedThenLastSourceEmailDateUpdates()
    {
        var repository = new InMemorySubscriptionRepository();
        var older = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1", emailDate: older) });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-2", emailDate: newer) });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Equal(newer.LocalDateTime, stored.LastSourceEmailDate);
    }

    [Fact]
    public void WhenDifferentMessageIdAddedThenRecurringEvidenceSummaryIsSet()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-2") });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Contains("Recurring monthly evidence: 2 similar emails from netflix.com", stored.RecurringEvidenceSummary);
    }

    // Case 4: with no reliable id, fall back to the original merge behavior.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenSameVendorWithoutReliableIdAddedAgainThenOccurrenceCountIncrements(string? messageId)
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: messageId) });

        repository.AddCandidates(new[] { CreateCandidate(messageId: messageId) });

        var stored = Assert.Single(repository.SuspectedCandidates);
        Assert.Equal(2, stored.OccurrenceCount);
    }

    // Case 5: candidates that are not the same subscription must not merge.

    [Fact]
    public void WhenDifferentVendorAddedThenEntriesAreNotMerged()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(vendor: "Netflix", messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(vendor: "Spotify", messageId: "msg-2") });

        Assert.Equal(2, repository.SuspectedCandidates.Count);
    }

    [Fact]
    public void WhenDifferentSourceAddedThenEntriesAreNotMerged()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(source: SubscriptionSource.Gmail, messageId: "msg-1") });

        repository.AddCandidates(new[] { CreateCandidate(source: SubscriptionSource.Outlook, messageId: "msg-2") });

        Assert.Equal(2, repository.SuspectedCandidates.Count);
    }

    // Case 6: ignoring a suspect removes it and prevents it from reappearing on a later scan.

    [Fact]
    public void WhenCandidateIgnoredThenItIsRemovedFromSuspectedList()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });
        var id = repository.SuspectedCandidates[0].Id;

        repository.IgnoreCandidate(id);

        Assert.Empty(repository.SuspectedCandidates);
    }

    [Fact]
    public void WhenCandidateIgnoredThenSignatureIsRecorded()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });
        var id = repository.SuspectedCandidates[0].Id;

        repository.IgnoreCandidate(id);

        Assert.Single(repository.IgnoredSignatures);
    }

    [Fact]
    public void WhenIgnoredSuspectRescannedThenItIsNotAddedBack()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });
        var id = repository.SuspectedCandidates[0].Id;
        repository.IgnoreCandidate(id);

        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-2") });

        Assert.Empty(repository.SuspectedCandidates);
    }

    [Fact]
    public void WhenIgnoredSuspectRescannedThenResultReportsDuplicate()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });
        var id = repository.SuspectedCandidates[0].Id;
        repository.IgnoreCandidate(id);

        var result = repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-2") });

        Assert.Equal(new CandidateAddResult(0, 1), result);
    }

    [Fact]
    public void WhenSignaturesRestoredThenMatchingSuspectIsSuppressedOnScan()
    {
        var seed = new InMemorySubscriptionRepository();
        seed.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });
        seed.IgnoreCandidate(seed.SuspectedCandidates[0].Id);
        var persisted = seed.IgnoredSignatures.ToArray();

        var restored = new InMemorySubscriptionRepository();
        restored.SetIgnoredSignatures(persisted);
        restored.AddCandidates(new[] { CreateCandidate(messageId: "msg-2") });

        Assert.Empty(restored.SuspectedCandidates);
    }

    [Fact]
    public void WhenDifferentVendorAddedThenIgnoredSuspectDoesNotSuppressIt()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(vendor: "Netflix", messageId: "msg-1", sender: "billing@netflix.com") });
        repository.IgnoreCandidate(repository.SuspectedCandidates[0].Id);

        repository.AddCandidates(new[] { CreateCandidate(vendor: "Spotify", messageId: "msg-2", sender: "billing@spotify.com") });

        Assert.Single(repository.SuspectedCandidates);
    }

    [Fact]
    public void WhenTestDataClearedThenIgnoredSignaturesAreCleared()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.AddCandidates(new[] { CreateCandidate(messageId: "msg-1") });
        repository.IgnoreCandidate(repository.SuspectedCandidates[0].Id);

        repository.ClearAllTestData();

        Assert.Empty(repository.IgnoredSignatures);
    }

    [Fact]
    public void WhenIgnoreCandidateCalledWithUnknownIdThenReturnsNull()
    {
        var repository = new InMemorySubscriptionRepository();

        var signature = repository.IgnoreCandidate(Guid.NewGuid());

        Assert.Null(signature);
    }

    private static SubscriptionCandidate CreateCandidate(
        string vendor = "Netflix",
        decimal? price = 15.99m,
        BillingCycle billingCycle = BillingCycle.Monthly,
        int confidence = 50,
        SubscriptionSource source = SubscriptionSource.Gmail,
        string? messageId = "msg-1",
        DateTimeOffset? emailDate = null,
        string? sender = "billing@netflix.com")
    {
        return new SubscriptionCandidate(
            Guid.NewGuid(),
            vendor,
            price,
            billingCycle,
            confidence,
            source,
            DetectionReason: $"Detected {vendor}",
            SourceEmailSender: sender,
            SourceEmailDate: emailDate,
            SourceMessageId: messageId);
    }
}
