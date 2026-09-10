using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The logo's two ornaments as a component. What these pin: each kind draws its own shape,
/// both are hidden from assistive technology (they are decoration, not content), and the page
/// template draws the rule exactly when it has a header to sit under.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class StonemarkTests : BunitContext
{
    [SetUp]
    public void SetUp() => Services.AddMudServices();

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private static RenderFragment Text(string text) => builder => builder.AddContent(0, text);

    [Test]
    public void Rule_DrawsTheGem_AndIsDecorative()
    {
        var cut = Render<Stonemark>(p => p.Add(m => m.Kind, StonemarkKind.Rule).Add(m => m.Class, "nornis-rule-short"));

        var rule = cut.Find(".nornis-rule");
        Assert.Multiple(() =>
        {
            Assert.That(rule.GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(rule.ClassList, Does.Contain("nornis-rule-short"), "placement classes pass through");
            Assert.That(cut.FindAll(".nornis-rule-gem"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll("svg"), Is.Empty);
        });
    }

    [Test]
    public void Branch_DrawsTheRune_AndIsDecorative()
    {
        var cut = Render<Stonemark>(p => p.Add(m => m.Kind, StonemarkKind.Branch));

        var svg = cut.Find("svg.nornis-mark-branch");
        Assert.Multiple(() =>
        {
            Assert.That(svg.GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(svg.GetAttribute("stroke"), Is.EqualTo("currentColor"), "the palette colours it, not the mark");
            Assert.That(cut.FindAll("path"), Has.Count.EqualTo(3), "stem and two pairs of branches");
            Assert.That(cut.FindAll(".nornis-rule"), Is.Empty);
        });
    }

    [Test]
    public void EntityPage_DrawsTheRuleUnderAHeader_AndNotWithout()
    {
        var withHeader = Render<EntityPage>(p => p
            .Add(e => e.Header, Text("the header"))
            .Add(e => e.Body, Text("the body")));
        var withoutHeader = Render<EntityPage>(p => p.Add(e => e.Body, Text("just a body")));

        Assert.Multiple(() =>
        {
            Assert.That(withHeader.FindAll(".nornis-entity-ornament"), Has.Count.EqualTo(1));
            Assert.That(withoutHeader.FindAll(".nornis-entity-ornament"), Is.Empty);
        });
    }
}
