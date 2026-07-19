using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactRelevanceTests
{
    [Test]
    public void Score_ExactNameMatch_ScoresHighest()
    {
        var score = ArtifactRelevance.Score("Captain Voss", null, "captain voss");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.ExactName));
    }

    [Test]
    public void Score_IgnoresSurroundingWhitespaceInTerm()
    {
        var score = ArtifactRelevance.Score("Captain Voss", null, "  Captain Voss  ");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.ExactName));
    }

    [Test]
    public void Score_NamePrefix_OutranksWholeWordInsideName()
    {
        var prefix = ArtifactRelevance.Score("Vossberg Keep", null, "voss");
        var inside = ArtifactRelevance.Score("Captain Voss", null, "voss");

        Assert.That(prefix, Is.EqualTo(ArtifactRelevance.NamePrefix));
        Assert.That(inside, Is.EqualTo(ArtifactRelevance.NameWord));
        Assert.That(prefix, Is.GreaterThan(inside));
    }

    [Test]
    public void Score_WholeWord_OutranksMidWordSubstring()
    {
        var wholeWord = ArtifactRelevance.Score("The Voss Ledger", null, "voss");
        var substring = ArtifactRelevance.Score("Ironvossen Guild", null, "voss");

        Assert.That(wholeWord, Is.EqualTo(ArtifactRelevance.NameWord));
        Assert.That(substring, Is.EqualTo(ArtifactRelevance.NameContains));
        Assert.That(wholeWord, Is.GreaterThan(substring));
    }

    [Test]
    public void Score_AnyNameMatch_OutranksAnySummaryMatch()
    {
        var nameMatch = ArtifactRelevance.Score("Ironvossen Guild", null, "voss");
        var summaryMatch = ArtifactRelevance.Score("Black Harbor", "Home of Captain Voss.", "voss");

        Assert.That(nameMatch, Is.GreaterThan(summaryMatch));
    }

    [Test]
    public void Score_MatchesSummaryWhenNameDoesNot()
    {
        var score = ArtifactRelevance.Score("Black Harbor", "Home of Captain Voss.", "voss");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.SummaryWord));
    }

    [Test]
    public void Score_MultiWordTermOutOfOrder_MatchesOnAllTokensPresent()
    {
        var score = ArtifactRelevance.Score("Captain Voss", null, "voss captain");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.NameAllTokens));
    }

    [Test]
    public void Score_MultiWordTermOutOfOrder_RanksBelowPhraseMatch()
    {
        var phrase = ArtifactRelevance.Score("Captain Voss of Black Harbor", null, "captain voss");
        var reordered = ArtifactRelevance.Score("Voss, the Captain", null, "captain voss");

        Assert.That(phrase, Is.GreaterThan(reordered));
    }

    [Test]
    public void Score_MultiWordTermAcrossSummary_MatchesOnAllTokensPresent()
    {
        var score = ArtifactRelevance.Score("Black Harbor", "Voss keeps a ledger here. She is a captain.", "voss captain");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.SummaryAllTokens));
    }

    [Test]
    public void Score_PartialTokenMatch_DoesNotMatch()
    {
        // "kraken" is absent, so the whole multi-word term fails even though "voss" hits.
        var score = ArtifactRelevance.Score("Captain Voss", "A harbor officer.", "voss kraken");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.NoMatch));
    }

    [Test]
    public void Score_NoOccurrenceAnywhere_ScoresNoMatch()
    {
        var score = ArtifactRelevance.Score("Black Harbor", "A port town.", "dragon");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.NoMatch));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Score_BlankTerm_ScoresNoMatch(string? term)
    {
        var score = ArtifactRelevance.Score("Captain Voss", "A harbor officer.", term);

        Assert.That(score, Is.EqualTo(ArtifactRelevance.NoMatch));
    }

    [Test]
    public void Score_NullName_FallsBackToSummary()
    {
        var score = ArtifactRelevance.Score(null, "Home of Captain Voss.", "voss");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.SummaryWord));
    }

    [Test]
    public void Score_NullNameAndSummary_ScoresNoMatch()
    {
        var score = ArtifactRelevance.Score(null, null, "voss");

        Assert.That(score, Is.EqualTo(ArtifactRelevance.NoMatch));
    }
}
