using Nornis.Application.Configuration;
using Nornis.Application.Prep;
using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Configuration;

[TestFixture]
public class PrepOptionsTests
{
    /// <summary>
    /// Requirement 2.5's promise: any Adventure within the bounds can be exported whole. An
    /// export is the sections' titles and bodies, a heading's markup per section and one filing
    /// line, so the most an export can come to has to fit the source body limit.
    /// </summary>
    [Test]
    public void AnAdventureAtEveryBound_StillFitsInOneSourceBody()
    {
        var options = new PrepOptions();

        var largestExport = options.MaxAdventureChars
            + options.MaxSections * AdventureSize.ExportOverheadPerSection
            + AdventureSize.ExportFilingLineAllowance;

        Assert.That(largestExport, Is.LessThanOrEqualTo(SourceService.MaxBodyChars));
    }

    [Test]
    public void OneSectionAtItsBound_FitsInTheAdventure()
    {
        var options = new PrepOptions();

        Assert.That(options.MaxSectionChars, Is.LessThan(options.MaxAdventureChars));
    }

    /// <summary>
    /// A turn's own material (the section on screen, the history window and the new message)
    /// is no larger than the world context the Loremaster already takes, at four characters a
    /// token, so a turn's prompt is at most about twice a draft's world block.
    /// </summary>
    [Test]
    public void ATurnsOwnMaterial_FitsInTheLoremastersContextBudget()
    {
        var options = new PrepOptions();
        var loremaster = new LoremasterOptions();

        var turn = options.ConversationSectionChars + options.ConversationHistoryChars + options.MaxConversationMessageChars;

        Assert.That(turn, Is.LessThanOrEqualTo(loremaster.MaxContextTokens * 4));
    }

    [Test]
    public void OneMessageAtItsBound_FitsInTheHistoryWindow()
    {
        var options = new PrepOptions();

        Assert.That(options.MaxConversationMessageChars, Is.LessThanOrEqualTo(options.ConversationHistoryChars));
    }

    [Test]
    public void TheDefaults_HaveNoNonPositiveBound() =>
        Assert.That(new PrepOptions().FirstNonPositiveBound(), Is.Null);

    [TestCase(nameof(PrepOptions.MaxConversationMessageChars))]
    [TestCase(nameof(PrepOptions.MaxConversationMessages))]
    [TestCase(nameof(PrepOptions.ConversationHistoryChars))]
    [TestCase(nameof(PrepOptions.ConversationSectionChars))]
    [TestCase(nameof(PrepOptions.MaxSectionChars))]
    [TestCase(nameof(PrepOptions.DesignOutputCeilingTokens))]
    [TestCase(nameof(PrepOptions.DesignTimeoutSeconds))]
    public void AZeroBound_IsNamed_SoTheApiRefusesToStart(string bound)
    {
        var options = new PrepOptions();
        typeof(PrepOptions).GetProperty(bound)!.SetValue(options, 0);

        Assert.That(options.FirstNonPositiveBound(), Is.EqualTo(bound));
    }

    [Test]
    public void TheLongestCall_IsTheLargestTimeout_SoNoLeaseLapsesUnderARunningTurn()
    {
        // Feature 41: the lease was the draft timeout plus 60 (150 s), under a 180 s research turn.
        var options = new PrepOptions { DraftTimeoutSeconds = 90, WebResearchTimeoutSeconds = 180, DesignTimeoutSeconds = 220 };

        Assert.That(options.LongestCallSeconds, Is.EqualTo(220));

        options.WebResearchTimeoutSeconds = 300;
        Assert.That(options.LongestCallSeconds, Is.EqualTo(300));
    }

    [Test]
    public void ADesignTurn_HasRoomForAWholeDraft()
    {
        // About four characters a token: the whole draft at its cap fits one answer's ceiling.
        Assert.That(new PrepOptions().DesignOutputCeilingTokens * 4, Is.GreaterThanOrEqualTo(Nornis.Domain.Entities.Adventure.MaxDraftChars));
    }
}
