using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class ArtifactTypeDisplayTests
{
    [TestCase("Active", false)]
    [TestCase("Dormant", true)]
    [TestCase("Resolved", true)]
    [TestCase("Archived", true)]
    public void IsWorthShowing_IsSilentOnlyForActive(string status, bool expected)
    {
        Assert.That(ArtifactTypeDisplay.IsWorthShowing(status), Is.EqualTo(expected));
    }

    [TestCase("Character", "nornis-type-character")]
    [TestCase("Storyline", "nornis-type-storyline")]
    [TestCase("Document", "nornis-type-other")]
    [TestCase("Spell", "nornis-type-other")]
    [TestCase(null, "nornis-type-other")]
    public void TypeClass_NamesTheTypesToken_AndSecondaryInkForTheRest(string? type, string expected)
    {
        Assert.That(ArtifactTypeDisplay.TypeClass(type), Is.EqualTo(expected));
    }
}
