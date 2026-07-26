using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The map is fully declarative — pins are absolutely-positioned elements rendered from
/// the parent's Placemarks list, with no JS marker state. So removing a pin only leaves
/// the screen if the *parent* re-renders, which is what makes OnPinRemoveRequested an
/// EventCallback rather than a plain delegate. These tests hold that line.
/// </summary>
[TestFixture]
// A BunitContext is single-use: NUnit's default one-instance-per-fixture would hand the
// second test an already-disposed renderer.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Category("Feature: map-source")]
public class MapViewerTests : BunitContext
{
    private static readonly MapPlacemarkDto Ironhold =
        new(Guid.NewGuid(), Guid.NewGuid(), "Ironhold", 0.2m, 0.3m, "Ironhold", null);

    private static readonly MapPlacemarkDto ThistleHold =
        new(Guid.NewGuid(), Guid.NewGuid(), "Thistle Hold", 0.6m, 0.7m, null, null);

    [SetUp]
    public void SetUp()
    {
        // MapViewer wires up the drag interop on first render when CanEdit is set.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => Dispose();

    [Test]
    public void RemoveClick_DropsThePinFromTheRenderedMap_WithoutAnExplicitStateHasChanged()
    {
        var pins = new List<MapPlacemarkDto> { Ironhold, ThistleHold };
        var cut = Render<PinHost>(ps => ps.Add(p => p.Pins, pins));

        Assert.That(cut.FindAll("a.nornis-map-pin"), Has.Count.EqualTo(2));

        cut.Find($"a[data-placemark-id=\"{Ironhold.Id}\"] button.nornis-map-pin-remove").Click();

        Assert.That(cut.Markup, Does.Not.Contain(Ironhold.Id.ToString()),
            "the removed pin must leave the map without a page refresh");
        Assert.That(cut.FindAll("a.nornis-map-pin"), Has.Count.EqualTo(1));
        Assert.That(cut.Markup, Does.Contain(ThistleHold.Id.ToString()));
    }

    [Test]
    public void WithoutARemoveHandler_NoRemoveButtonIsOffered()
    {
        var cut = Render<MapViewer>(ps => ps
            .Add(p => p.ImageUrl, "https://example.test/map.png")
            .Add(p => p.Placemarks, new List<MapPlacemarkDto> { Ironhold })
            .Add(p => p.CanEdit, true));

        Assert.That(cut.FindAll("button.nornis-map-pin-remove"), Is.Empty);
    }

    [Test]
    public void WithoutEditRights_NoRemoveButtonIsOffered()
    {
        var cut = Render<PinHost>(ps => ps
            .Add(p => p.Pins, new List<MapPlacemarkDto> { Ironhold })
            .Add(p => p.CanEdit, false));

        Assert.That(cut.FindAll("button.nornis-map-pin-remove"), Is.Empty);
    }

    /// <summary>
    /// Stands in for SourceDetail: it owns the pin list and hands MapViewer a fresh copy
    /// each render. Its remove handler mutates that list and — deliberately — never calls
    /// StateHasChanged, so only EventCallback's post-event re-render can update the map.
    /// </summary>
    private sealed class PinHost : ComponentBase
    {
        [Parameter] public List<MapPlacemarkDto> Pins { get; set; } = [];

        [Parameter] public bool CanEdit { get; set; } = true;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MapViewer>(0);
            builder.AddComponentParameter(1, nameof(MapViewer.ImageUrl), "https://example.test/map.png");
            builder.AddComponentParameter(2, nameof(MapViewer.Placemarks), (IReadOnlyList<MapPlacemarkDto>)[.. Pins]);
            builder.AddComponentParameter(3, nameof(MapViewer.CanEdit), CanEdit);
            builder.AddComponentParameter(4, nameof(MapViewer.OnPinRemoveRequested),
                EventCallback.Factory.Create<MapPlacemarkDto>(this, RemovePin));
            builder.CloseComponent();
        }

        private void RemovePin(MapPlacemarkDto pin) => Pins.RemoveAll(p => p.Id == pin.Id);
    }
}
