using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using Xunit;

namespace SubscriptionTiger.Tests;

public class SubscriptionSignalAnalyzerTests
{
    private const string MismatchSubject = "Your Netflix subscription receipt";
    private const string MismatchSnippet = "Your monthly Netflix subscription payment was charged. Manage subscription anytime.";

    [Fact]
    public void WhenBrandInTextButSenderDomainUntrustedThenVendorIsNotBrand()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Netflix <billing@ardentrange.com>",
            MismatchSubject,
            MismatchSnippet,
            out _,
            out _,
            out _,
            out _,
            out var vendor);

        Assert.NotEqual("Netflix", vendor);
    }

    [Fact]
    public void WhenBrandInTextButSenderDomainUntrustedThenVendorIsDomainDerived()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Netflix <billing@ardentrange.com>",
            MismatchSubject,
            MismatchSnippet,
            out _,
            out _,
            out _,
            out _,
            out var vendor);

        Assert.Equal("Ardentrange", vendor);
    }

    [Fact]
    public void WhenBrandInTextButSenderDomainUntrustedThenReasonExplainsMismatch()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Netflix <billing@ardentrange.com>",
            MismatchSubject,
            MismatchSnippet,
            out _,
            out var reason,
            out _,
            out _,
            out _);

        Assert.Contains(
            "Brand mention only: Netflix mentioned, sender domain ardentrange.com not trusted for Netflix",
            reason);
    }

    [Fact]
    public void WhenBrandInTextButSenderDomainUntrustedThenConfidenceIsNotHigh()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Netflix <billing@ardentrange.com>",
            MismatchSubject,
            MismatchSnippet,
            out var confidence,
            out _,
            out _,
            out _,
            out _);

        Assert.True(confidence < 80, $"Expected confidence below High band but was {confidence}.");
    }

    [Fact]
    public void WhenBrandMatchesTrustedDomainThenVendorRemainsBrand()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Microsoft <billing@microsoft.com>",
            "Your Microsoft invoice is ready",
            "Your monthly Microsoft 365 subscription was charged $9.99. Invoice attached.",
            out _,
            out _,
            out _,
            out _,
            out var vendor);

        Assert.Equal("Microsoft", vendor);
    }

    [Fact]
    public void WhenBrandMatchesTrustedDomainThenReasonHasNoMismatchWarning()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Microsoft <billing@microsoft.com>",
            "Your Microsoft invoice is ready",
            "Your monthly Microsoft 365 subscription was charged $9.99. Invoice attached.",
            out _,
            out var reason,
            out _,
            out _,
            out _);

        Assert.DoesNotContain("Brand mention only", reason);
    }

    [Fact]
    public void WhenBrandMatchesTrustedDomainThenSenderMatchIsCredited()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Microsoft <billing@microsoft.com>",
            "Your Microsoft invoice is ready",
            "Your monthly Microsoft 365 subscription was charged $9.99. Invoice attached.",
            out _,
            out var reason,
            out _,
            out _,
            out _);

        Assert.Contains("sender matched Microsoft", reason);
    }

    [Fact]
    public void WhenBrandMatchesTrustedDomainThenHighConfidenceIsAllowed()
    {
        var analyzer = new SubscriptionSignalAnalyzer();

        analyzer.TryAnalyze(
            "Microsoft <billing@microsoft.com>",
            "Your Microsoft invoice receipt is ready",
            "Your monthly Microsoft 365 subscription payment was charged $9.99. Invoice and receipt attached.",
            out var confidence,
            out _,
            out _,
            out _,
            out _);

        Assert.True(confidence >= 80, $"Expected High confidence but was {confidence}.");
    }

    [Fact]
    public void WhenRepeatedWeakBrandMentionsFromUntrustedDomainThenNoEntryIsBrand()
    {
        var analyzer = new SubscriptionSignalAnalyzer();
        var candidates = new[]
        {
            CreateWeakNetflixCandidate(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero)),
            CreateWeakNetflixCandidate(new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.Zero)),
            CreateWeakNetflixCandidate(new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero))
        };

        var enriched = analyzer.ApplyRecurringEvidence(candidates);

        Assert.DoesNotContain(enriched, x => x.Vendor == "Netflix");
    }

    [Fact]
    public void WhenRepeatedWeakBrandMentionsFromUntrustedDomainThenEntriesAttachToSenderDomain()
    {
        var analyzer = new SubscriptionSignalAnalyzer();
        var candidates = new[]
        {
            CreateWeakNetflixCandidate(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero)),
            CreateWeakNetflixCandidate(new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.Zero)),
            CreateWeakNetflixCandidate(new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero))
        };

        var enriched = analyzer.ApplyRecurringEvidence(candidates);

        Assert.All(enriched, x => Assert.Equal("Ardentrange", x.Vendor));
    }

    private static SubscriptionCandidate CreateWeakNetflixCandidate(DateTimeOffset emailDate)
    {
        var analyzer = new SubscriptionSignalAnalyzer();
        analyzer.TryAnalyze(
            "Netflix <billing@ardentrange.com>",
            MismatchSubject,
            MismatchSnippet,
            out var confidence,
            out var reason,
            out var price,
            out var billingCycle,
            out var vendor);

        return new SubscriptionCandidate(
            Guid.NewGuid(),
            vendor,
            price,
            billingCycle,
            confidence,
            SubscriptionSource.Outlook,
            reason,
            SourceEmailSubject: MismatchSubject,
            SourceEmailSender: "Netflix <billing@ardentrange.com>",
            SourceEmailDate: emailDate,
            SourceEmailSnippet: MismatchSnippet,
            SourceMessageId: Guid.NewGuid().ToString());
    }
}
