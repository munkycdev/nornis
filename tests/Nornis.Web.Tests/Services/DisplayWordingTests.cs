using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// The small display rules the 3.32 vocabulary pass put in one place each: money, confidence,
/// the name of a source's date, what Reveal says is hidden, and a campaign's span in a list.
/// </summary>
[TestFixture]
public class DisplayWordingTests
{
    // Amounts and confidences are written as text, which NUnit reads as the decimal it says.
    // From .NET 11 a double converts to a decimal exactly, so 0.6 would arrive as
    // 0.59999999999999997779553950 and count as low.
    [TestCase("0", "$0.00")]
    [TestCase("0.004", "under $0.01")]
    [TestCase("0.005", "$0.01")]
    [TestCase("0.25", "$0.25")]
    [TestCase("12.5", "$12.50")]
    public void Money_HasTwoDecimals_AndNeverCallsASmallSpendNothing(decimal usd, string expected)
    {
        Assert.That(DisplayText.Money(usd), Is.EqualTo(expected));
    }

    [TestCase("0.97", null)]
    [TestCase("0.6", null)]
    [TestCase("0.45", "Low confidence (45%)")]
    [TestCase(null, null)]
    public void Confidence_IsSaidOnlyWhenItIsLow(decimal? confidence, string? expected)
    {
        Assert.That(DisplayText.LowConfidence(confidence), Is.EqualTo(expected));
    }

    [TestCase("SessionNote", "Session date")]
    [TestCase("HandwrittenNotes", "Session date")]
    [TestCase("ImportedNote", "Session date")]
    [TestCase("SessionAudio", "Session date")]
    [TestCase("GMNote", "Date")]
    [TestCase("Map", "Date")]
    [TestCase("SomethingNew", "Date")]
    public void ASourcesDate_IsASessionDate_OnlyOnASession(string type, string expected)
    {
        Assert.That(SourceTypeDisplay.DateLabel(type), Is.EqualTo(expected));
    }

    [TestCase("Relationship", "BrokersFor → Captain Voss", "brokers for → Captain Voss")]
    [TestCase("Relationship", "no arrow here", "no arrow here")]
    [TestCase("Fact", "LocatedIn → the harbor", "LocatedIn → the harbor")]
    public void WhatRevealSaysIsHidden_ReadsARelationshipInWords(string kind, string description, string expected)
    {
        var candidate = new ConvergenceCandidateDto(kind, Guid.NewGuid(), Guid.NewGuid(), "Captain Voss", description,
            DateTimeOffset.UtcNow, [], new ConvergenceComponentsDto(0, 0, 0, true, null, null, false, 0, 0, 0, 0, null), 50, null);

        Assert.That(ConvergenceDisplay.Description(candidate), Is.EqualTo(expected));
    }

    [Test]
    public void ACampaignsSpanInTheList_ComesFromItsOwnSources()
    {
        var campaign = Guid.NewGuid();
        var other = Guid.NewGuid();
        SourceListItem[] sources =
        [
            Source(campaign, new DateTimeOffset(2026, 3, 22, 0, 0, 0, TimeSpan.Zero)),
            Source(campaign, new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)),
            Source(campaign, occurredAt: null),
            Source(other, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            Source(campaignId: null, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        ];

        var spans = CampaignDisplay.SessionSpans(sources);

        Assert.Multiple(() =>
        {
            Assert.That(spans.Keys, Is.EquivalentTo((Guid[])[campaign, other]), "a source filed under no campaign belongs to none");
            Assert.That(spans[campaign].Count, Is.EqualTo(3));
            Assert.That(spans[campaign].First, Is.EqualTo(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)), "an undated source does not date the span");
            Assert.That(spans[campaign].Last, Is.EqualTo(new DateTimeOffset(2026, 3, 22, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    private static SourceListItem Source(Guid? campaignId, DateTimeOffset? occurredAt) => new(
        Guid.NewGuid(), Guid.NewGuid(), "SessionNote", "A session", occurredAt, DateTimeOffset.UtcNow, Guid.NewGuid(),
        "PartyVisible", "Processed", campaignId);
}
