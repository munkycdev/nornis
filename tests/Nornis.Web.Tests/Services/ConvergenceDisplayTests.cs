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

    #region RelativeFill

    [Test]
    public void RelativeFill_DrawsTheStrongestCandidateFull()
    {
        // The whole point: on a page where 31 is the best there is, 31 must read as the best
        // there is. A real world capped every score near a third and the page read as "none of
        // this matters".
        Assert.That(ConvergenceDisplay.RelativeFill(31, 31), Is.EqualTo(100));
    }

    [Test]
    public void RelativeFill_ScalesTheRestAgainstIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceDisplay.RelativeFill(21, 31), Is.EqualTo(68));
            Assert.That(ConvergenceDisplay.RelativeFill(12, 31), Is.EqualTo(39));
            Assert.That(ConvergenceDisplay.RelativeFill(2, 31), Is.EqualTo(6));
        });
    }

    [Test]
    public void RelativeFill_KeepsTheOrderTheScoreGave()
    {
        int[] scores = [31, 31, 21, 18, 12, 2];
        var fills = scores.Select(x => ConvergenceDisplay.RelativeFill(x, 31)).ToList();

        Assert.That(fills.Zip(fills.Skip(1)).All(p => p.First >= p.Second), Is.True,
            "rescaling may not reorder what the score decided");
    }

    [TestCase(0, 0)]
    [TestCase(0, 40)]
    [TestCase(15, 0)]
    public void RelativeFill_IsZeroWhenThereIsNothingToScaleAgainst(int score, int topScore)
    {
        // A gauge whose best candidate scores nothing must not draw a full ring for it — that
        // is the lie normalising the number would have told.
        Assert.That(ConvergenceDisplay.RelativeFill(score, topScore), Is.Zero);
    }

    [Test]
    public void ScoreColor_StaysAbsoluteSoAWeakFieldStaysMuted()
    {
        // Full ring, muted colour: "the best thing available, and it is not urgent".
        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceDisplay.RelativeFill(31, 31), Is.EqualTo(100));
            Assert.That(ConvergenceDisplay.ScoreColor(31), Is.EqualTo(MudBlazor.Color.Secondary));
            Assert.That(ConvergenceDisplay.ScoreColor(75), Is.EqualTo(MudBlazor.Color.Primary));
        });
    }

    #endregion

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
