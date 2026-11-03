using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// Feature 38, Requirement 1: what an Adventure records about AI. Recorded use outranks the GM's
/// own tick, and only recorded use locks the box.
/// </summary>
[TestFixture]
public class AdventureAiUseTests
{
    [TestCase(false, false, false, AiUseKind.None, false)]
    [TestCase(false, false, true, AiUseKind.Declared, false)]
    [TestCase(true, false, false, AiUseKind.Text, true)]
    [TestCase(false, true, false, AiUseKind.Art, true)]
    [TestCase(true, true, false, AiUseKind.TextAndArt, true)]
    [TestCase(true, false, true, AiUseKind.Text, true)]
    [TestCase(true, true, true, AiUseKind.TextAndArt, true)]
    public void Kind_PutsRecordedUseBeforeTheGmsTick(bool wroteText, bool madeArt, bool declared, AiUseKind kind, bool locked)
    {
        var use = new AdventureAiUse { WroteText = wroteText, MadeArt = madeArt, Declared = declared };

        Assert.Multiple(() =>
        {
            Assert.That(use.Kind(), Is.EqualTo(kind));
            Assert.That(use.IsLocked(), Is.EqualTo(locked));
        });
    }

    /// <summary>
    /// Requirement 1.2 and 1.5: an applied edit and Put in draft are the Loremaster's words; a
    /// gather, a split and an import only move the GM's own. Every kind of change is classified,
    /// so a new one can't record (or fail to record) AI use by accident.
    /// </summary>
    [Test]
    public void EveryDraftChange_IsClassified_AndOnlyEditAndPutAreTheLoremastersWords()
    {
        var writers = Enum.GetValues<AdventureDraftChange>().Where(c => c.IsLoremasterWriting()).ToList();

        Assert.That(writers, Is.EquivalentTo([AdventureDraftChange.Edit, AdventureDraftChange.Put]));
    }

    [Test]
    public void AnUnknownDraftChange_Throws()
    {
        Assert.That(() => ((AdventureDraftChange)99).IsLoremasterWriting(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
