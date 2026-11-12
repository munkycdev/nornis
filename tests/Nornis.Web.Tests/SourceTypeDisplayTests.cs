using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

[TestFixture]
public class SourceTypeDisplayTests
{
    [Test]
    public void CaptureOptions_IncludeMap()
    {
        Assert.That(SourceTypeDisplay.CaptureOptions.Any(o => o.Value == "Map"), Is.True);
        Assert.That(SourceTypeDisplay.IsCaptureOption("Map"), Is.True);
    }

    [Test]
    public void Label_MapsKnownTypes()
    {
        Assert.That(SourceTypeDisplay.Label("Map"), Is.EqualTo("Map"));
        Assert.That(SourceTypeDisplay.Label("HandwrittenNotes"), Is.EqualTo("Handwritten note"));
        Assert.That(SourceTypeDisplay.Label("GMNote"), Is.EqualTo("GM note"));
        Assert.That(SourceTypeDisplay.Label("AdventureExport"), Is.EqualTo("Adventure prep"));
        Assert.That(SourceTypeDisplay.IsCaptureOption("AdventureExport", isGm: true), Is.False, "an export is filed from Prep, never captured");
    }

    [Test]
    public void GmNote_IsOfferedOnlyToAGm()
    {
        Assert.That(SourceTypeDisplay.CaptureOptionsFor(isGm: true).Any(o => o.Value == "GMNote"), Is.True);
        Assert.That(SourceTypeDisplay.CaptureOptionsFor(isGm: false).Any(o => o.Value == "GMNote"), Is.False);
        Assert.That(SourceTypeDisplay.IsCaptureOption("GMNote", isGm: false), Is.False);
        Assert.That(SourceTypeDisplay.IsCaptureOption("SessionNote", isGm: false), Is.True);
    }

    [Test]
    public void LegacyTypes_AreLabelledButNotOffered()
    {
        Assert.That(SourceTypeDisplay.Label("JournalEntry"), Is.EqualTo("Journal entry"));
        Assert.That(SourceTypeDisplay.IsCaptureOption("JournalEntry"), Is.False);
    }
}
