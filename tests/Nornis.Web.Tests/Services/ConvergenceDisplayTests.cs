using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// The phrases beside a candidate's score are the GM's whole basis for disagreeing with the
/// ranking, so they have to describe the same observations the score was built from. Every one
/// is read from the API's response; none is recomputed here.
/// </summary>
[TestFixture]
public class ConvergenceDisplayTests
{
    [Test]
    public void Phrases_LeadWithTheContradiction()
    {
        var phrases = ConvergenceDisplay.Phrases(Candidate(contradictionSeverity: "High"));

        Assert.That(phrases[0], Does.Contain("contradicts what the party believes").And.Contain("high"),
            "the reveal with a deadline is the reason to look at this row at all");
    }

    [Test]
    public void Phrases_OmitTheContradictionWhenThereIsNone()
    {
        var phrases = ConvergenceDisplay.Phrases(Candidate(contradictionSeverity: null));

        Assert.That(phrases, Has.None.Contains("contradicts"));
    }

    [TestCase(0, "hidden since today")]
    [TestCase(1, "hidden for a day")]
    [TestCase(94, "hidden for 94 days")]
    public void Phrases_ReadDaysHiddenNaturally(int days, string expected)
    {
        Assert.That(ConvergenceDisplay.Phrases(Candidate(daysHidden: days)), Does.Contain(expected));
    }

    [Test]
    public void Phrases_SayWhenARevealStandsAlone()
    {
        var phrases = ConvergenceDisplay.Phrases(Candidate(missingArtifactCount: 0, isSelfContained: true));

        Assert.That(phrases, Does.Contain("reveals cleanly on its own"));
    }

    [TestCase(1, "brings 1 other entry with it")]
    [TestCase(3, "brings 3 other entries with it")]
    public void Phrases_CountWhatARevealDragsAlong(int missing, string expected)
    {
        var phrases = ConvergenceDisplay.Phrases(
            Candidate(missingArtifactCount: missing, isSelfContained: false));

        Assert.That(phrases, Does.Contain(expected));
    }

    [Test]
    public void Phrases_WarnWhenThePartyHasNeverMetTheEntry()
    {
        // The one phrase that explains a low score rather than a high one — without it the
        // familiarity gate looks like the gauge ignoring an obviously old secret.
        var phrases = ConvergenceDisplay.Phrases(Candidate(partyVisibleFactsOnAnchor: 0));

        Assert.That(phrases, Does.Contain("the party has not met this entry"));
    }

    [Test]
    public void Phrases_StaySilentAboutFamiliarityWhenThePartyKnowsTheEntry()
    {
        var phrases = ConvergenceDisplay.Phrases(Candidate(partyVisibleFactsOnAnchor: 4));

        Assert.That(phrases, Has.None.Contains("has not met"));
    }

    [Test]
    public void Phrases_NameTheStorylineStateWhenThereIsOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceDisplay.Phrases(Candidate(storylineStatus: "Resolved")),
                Does.Contain("storyline resolved"));
            Assert.That(ConvergenceDisplay.Phrases(Candidate(storylineStatus: null)),
                Has.None.Contains("storyline"));
        });
    }

    private static ConvergenceCandidateDto Candidate(
        int daysHidden = 30,
        int partyVisibleFactsOnAnchor = 3,
        int missingArtifactCount = 0,
        bool isSelfContained = true,
        string? storylineStatus = null,
        string? contradictionSeverity = null) => new(
        "Fact",
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Captain Voss",
        "true allegiance: sworn to the Vespergale cult",
        DateTimeOffset.UtcNow.AddDays(-daysHidden),
        [],
        new ConvergenceComponentsDto(
            daysHidden,
            partyVisibleFactsOnAnchor,
            missingArtifactCount,
            isSelfContained,
            storylineStatus,
            contradictionSeverity,
            ContradictionAssessed: contradictionSeverity is not null,
            Dormancy: 0.5,
            AnchorFamiliarity: 0.6,
            SelfContainment: 1.0,
            StorylineState: 0.0,
            ContradictionPressure: contradictionSeverity is null ? null : 1.0),
        Score: 55,
        Rationale: null);
}
