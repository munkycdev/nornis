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
        Assert.That(SourceTypeDisplay.Label("HandwrittenNotes"), Is.EqualTo("Handwritten Note"));
        Assert.That(SourceTypeDisplay.Label("GMNote"), Is.EqualTo("GM Note"));
    }

    [Test]
    public void LegacyTypes_AreLabelledButNotOffered()
    {
        Assert.That(SourceTypeDisplay.Label("JournalEntry"), Is.EqualTo("Journal Entry"));
        Assert.That(SourceTypeDisplay.IsCaptureOption("JournalEntry"), Is.False);
    }
}
