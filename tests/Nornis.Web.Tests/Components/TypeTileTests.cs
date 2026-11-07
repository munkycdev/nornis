using Bunit;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// Feature 42's type tile: the type's icon, coloured through the type's class rather than by
/// value, and decorative, since the entry's name beside it says what it is.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class TypeTileTests : BunitContext
{
    [TearDown]
    public void TearDown() => Dispose();

    [TestCase("Location", "nornis-type-location")]
    [TestCase("Document", "nornis-type-other")]
    [TestCase(null, "nornis-type-other")]
    public void TheTile_CarriesItsTypesClass(string? type, string expected)
    {
        var cut = Render<TypeTile>(ps => ps.Add(p => p.Type, type));

        var tile = cut.Find(".nornis-type-tile");
        Assert.Multiple(() =>
        {
            Assert.That(tile.ClassList, Does.Contain(expected));
            Assert.That(tile.GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(tile.GetAttribute("style"), Is.Null, "never a colour by value");
            Assert.That(tile.QuerySelector("svg"), Is.Not.Null);
        });
    }

    [Test]
    public void TheLargeTile_IsTheEntryHeadersSize()
    {
        var cut = Render<TypeTile>(ps => ps.Add(p => p.Type, "Character").Add(p => p.Large, true));

        Assert.That(cut.Find(".nornis-type-tile").ClassList, Does.Contain("nornis-type-tile-lg"));
    }
}
