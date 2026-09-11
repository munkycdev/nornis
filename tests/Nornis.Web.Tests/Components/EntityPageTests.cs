using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The page template (feature 24, phase B) has three slots and one shape. These pin the shape:
/// the header spans the top, the body and rail sit side by side, and a page that supplies no
/// rail gets no empty column. The rail's own sections appear only when they have something to
/// say — an empty "Connected" heading over nothing would be the template talking to itself.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class EntityPageTests : BunitContext
{
    [SetUp]
    public void SetUp() => Services.AddMudServices();

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private static RenderFragment Text(string text) => builder => builder.AddContent(0, text);

    [Test]
    public void ThreeSlots_LandInTheirRegions()
    {
        var cut = Render<EntityPage>(p => p
            .Add(e => e.Header, Text("the header"))
            .Add(e => e.Body, Text("the body"))
            .Add(e => e.Rail, Text("the rail")));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-entity-header").TextContent, Is.EqualTo("the header"));
            Assert.That(cut.Find(".nornis-entity-body").TextContent, Is.EqualTo("the body"));
            Assert.That(cut.Find("aside.nornis-entity-rail").TextContent, Is.EqualTo("the rail"));
        });
    }

    [Test]
    public void NoRail_NoColumn()
    {
        var cut = Render<EntityPage>(p => p.Add(e => e.Body, Text("just a body")));

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("aside.nornis-entity-rail"), Is.Empty);
            Assert.That(cut.FindAll(".nornis-entity-header"), Is.Empty);
            Assert.That(cut.Find(".nornis-entity-body").TextContent, Is.EqualTo("just a body"));
        });
    }

    [Test]
    public void Rail_RendersRowsAndLinks()
    {
        var cut = Render<ContextRail>(p => p
            .Add(r => r.RowsTitle, "This thing")
            .Add(r => r.Rows, new List<ContextRail.RailRow>
            {
                new("Sources", "12", "/sources"),
                new("Facts", "3"),
            })
            .Add(r => r.RelatedTitle, "Connected")
            .Add(r => r.Related, new List<ContextRail.RailLink>
            {
                new("icon", "Fera", "/codex/1"),
            })
            .Add(r => r.RelatedMoreHref, "/codex")
            .Add(r => r.ShowLoremaster, false));

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-rail-title").Select(t => t.TextContent), Is.EqualTo(["This thing", "Connected"]));
            Assert.That(cut.FindAll(".nornis-rail-row"), Has.Count.EqualTo(2));
            Assert.That(cut.Find(".nornis-rail-row a").GetAttribute("href"), Is.EqualTo("/sources"), "a row with somewhere to go links");
            Assert.That(cut.FindAll(".nornis-rail-value").Select(v => v.TextContent), Is.EqualTo(["12", "3"]));
            Assert.That(cut.Find(".nornis-rail-link").GetAttribute("href"), Is.EqualTo("/codex/1"));
            Assert.That(cut.Find(".nornis-rail-more").GetAttribute("href"), Is.EqualTo("/codex"));
        });
    }

    [Test]
    public void Rail_WithNothingToSay_ShowsNoHeadings()
    {
        var cut = Render<ContextRail>(p => p.Add(r => r.ShowLoremaster, false));

        Assert.That(cut.FindAll(".nornis-rail-section"), Is.Empty);
    }
}
