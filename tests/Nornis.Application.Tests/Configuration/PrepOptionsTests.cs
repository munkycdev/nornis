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
}
