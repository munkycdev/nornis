using Bunit;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Locations map says two things about a place before it is clicked: whether anything has
/// happened there, and how much. The colour carries the first, the pin's tip carries both —
/// so a place with a history can never be legible only to someone who can see gold.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Category("Feature: locations")]
public class LocationsMapExplorerTests : BunitContext
{
    private static readonly Guid BlackHarbor = Guid.NewGuid();
    private static readonly Guid Thornwatch = Guid.NewGuid();

    [TearDown]
    public void TearDown() => Dispose();

    private static JourneyDto Journey(params JourneyStopDto[] stops) =>
        new(
            MapAttachmentId: Guid.NewGuid(),
            MapSourceId: Guid.NewGuid(),
            ImageUrl: "https://example.test/map.png",
            Locations:
            [
                new JourneyLocationDto(BlackHarbor, "Black Harbor", 0.2m, 0.3m, null),
                new JourneyLocationDto(Thornwatch, "Thornwatch", 0.6m, 0.7m, null),
            ],
            Stops: stops,
            UndatedSessionCount: 0);

    private static JourneyStopDto Stop(string title, params Guid[] visited) =>
        new(Guid.NewGuid(), title, DateTimeOffset.UtcNow, visited, []);

    [Test]
    public void PlaceWithSessions_IsMarkedApartFromOneNothingHasHappenedIn()
    {
        var cut = Render<LocationsMapExplorer>(ps => ps
            .Add(p => p.Journey, Journey(Stop("The docks", BlackHarbor))));

        var pins = cut.FindAll("button.nornis-journey-pin");
        Assert.That(pins, Has.Count.EqualTo(2));

        var visited = pins.Single(p => p.GetAttribute("aria-label")!.StartsWith("Black Harbor"));
        var untouched = pins.Single(p => p.GetAttribute("aria-label")!.StartsWith("Thornwatch"));

        Assert.That(visited.ClassList, Does.Contain("has-history"));
        Assert.That(untouched.ClassList, Does.Not.Contain("has-history"));
    }

    [Test]
    public void PinTip_CountsSessions_SoTheColourIsNeverTheOnlyTelling()
    {
        var cut = Render<LocationsMapExplorer>(ps => ps
            .Add(p => p.Journey, Journey(
                Stop("The docks", BlackHarbor),
                // The same place twice in one stop is still one visit.
                Stop("Back to the docks", BlackHarbor, BlackHarbor))));

        var pins = cut.FindAll("button.nornis-journey-pin");

        Assert.That(pins.Single(p => p.GetAttribute("aria-label")!.StartsWith("Black Harbor"))
            .GetAttribute("aria-label"), Is.EqualTo("Black Harbor — 2 sessions"));
        Assert.That(pins.Single(p => p.GetAttribute("aria-label")!.StartsWith("Thornwatch"))
            .GetAttribute("aria-label"), Is.EqualTo("Thornwatch — not visited yet"));
    }

    [Test]
    public void ANewJourney_RecountsVisits()
    {
        // The counts are cached per journey, so a world switch that reuses the component must
        // not leave the previous world's history colouring the new map.
        var cut = Render<LocationsMapExplorer>(ps => ps
            .Add(p => p.Journey, Journey(Stop("The docks", BlackHarbor))));

        cut.Render(ps => ps.Add(p => p.Journey, Journey(Stop("The watchtower", Thornwatch))));

        var pins = cut.FindAll("button.nornis-journey-pin");
        Assert.That(pins.Single(p => p.GetAttribute("aria-label")!.StartsWith("Black Harbor")).ClassList,
            Does.Not.Contain("has-history"));
        Assert.That(pins.Single(p => p.GetAttribute("aria-label")!.StartsWith("Thornwatch")).ClassList,
            Does.Contain("has-history"));
    }
}
