using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The hero band (feature 42) lays out what the API decided: key art through the Web's stamped
/// route, a map by the URL the Map page uses, or no picture; the anchor as the crop's focus. The
/// facts line says what the caller can see of the world's play.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HeroBandTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly DateTimeOffset Stamp = DateTimeOffset.Parse("2026-11-07T20:00:00Z");

    [SetUp]
    public void SetUp() => Services.AddMudServices();

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private static WorldHeroDto Hero(HeroPictureDto picture, int sessions = 12, int entries = 140, string? system = "Shadowdark",
        DateTimeOffset? last = null) =>
        new(WorldId, "Black Harbor", system, sessions, last ?? DateTimeOffset.Parse("2026-11-01T19:00:00Z"), entries, picture);

    private static readonly HeroPictureDto KeyArt = new("KeyArt", Stamp, null, 0);
    private static readonly HeroPictureDto Map = new("Map", null, "https://blobs/map.png?sig=x", 50);
    private static readonly HeroPictureDto None = new("None", null, null, 50);

    private IRenderedComponent<HeroBand> RenderBand(WorldHeroDto hero, string? title = "Black Harbor", string? publicSlug = null) =>
        Render<HeroBand>(p => p
            .Add(x => x.Title, title)
            .Add(x => x.Hero, hero)
            .Add(x => x.PictureUrl, HeroDisplay.PictureUrl(hero, publicSlug))
            .Add(x => x.Caption, "Ask the Loremaster")
            .AddChildContent("<div class=\"nornis-ask\">ask</div>"));

    [Test]
    public void KeyArt_IsShownByItsStampedRoute_WithTheAnchorAsTheFocus()
    {
        var cut = RenderBand(Hero(KeyArt));

        var img = cut.Find(".nornis-heroband-img");
        Assert.Multiple(() =>
        {
            Assert.That(img.GetAttribute("src"), Is.EqualTo($"/world-art/{WorldId}?v={Stamp.ToUnixTimeMilliseconds()}"));
            Assert.That(img.GetAttribute("style"), Does.Contain("center 0%"));
            Assert.That(img.GetAttribute("alt"), Is.Empty, "the picture is decoration beside the world's name");
            Assert.That(cut.Find(".nornis-heroband").ClassList, Does.Contain("nornis-heroband-pictured"));
            Assert.That(cut.FindAll(".nornis-heroband-picture-map"), Is.Empty);
            Assert.That(cut.Find("h1.nornis-heroband-title").TextContent, Is.EqualTo("Black Harbor"));
            Assert.That(cut.Find(".nornis-heroband-body .nornis-ask"), Is.Not.Null);
        });
    }

    [Test]
    public void OnThePublicPage_KeyArtGoesThroughThePublicRoute()
    {
        var cut = RenderBand(Hero(KeyArt), title: null, publicSlug: "black-harbor");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-heroband-img").GetAttribute("src"), Does.StartWith("/w/black-harbor/art?v="));
            Assert.That(cut.FindAll(".nornis-heroband-title"), Is.Empty, "the public frame already names the world");
        });
    }

    [Test]
    public void AMap_IsShownByItsOwnUrl_AndMarkedAsAMap()
    {
        var cut = RenderBand(Hero(Map));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-heroband-img").GetAttribute("src"), Is.EqualTo("https://blobs/map.png?sig=x"));
            Assert.That(cut.FindAll(".nornis-heroband-picture-map"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void WithNoPicture_TheBandIsTypeset_WithTheMark()
    {
        var cut = RenderBand(Hero(None));

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-heroband-img"), Is.Empty);
            Assert.That(cut.Find(".nornis-heroband").ClassList, Does.Contain("nornis-heroband-plain"));
            Assert.That(cut.FindAll(".nornis-hero-mark"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void TheFacts_AreTheSystemSessionsEntriesAndLastPlayed()
    {
        var cut = RenderBand(Hero(None));

        Assert.That(cut.Find(".nornis-heroband-facts").TextContent,
            Is.EqualTo("Shadowdark · 12 sessions · 140 codex entries · last played 1 Nov 2026"));
    }

    [Test]
    public void TheFacts_AreSingularForOne_AndLeaveOutWhatIsUnknown()
    {
        var hero = Hero(None, sessions: 1, entries: 1, system: null) with { LastSessionAt = null };

        Assert.That(HeroDisplay.Facts(hero), Is.EqualTo((string[])["1 session", "1 codex entry"]));
    }

    [Test]
    public void AMapWithNoUrl_IsNoPicture() =>
        Assert.That(HeroDisplay.PictureUrl(Hero(Map with { MapUrl = "" })), Is.Null);
}
