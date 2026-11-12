using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// An AI call's error (feature 39): a refusal is the GM's to fix, so it says how; anything else
/// stays an error with no link that would send them hunting for a key that isn't the problem.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AiErrorAlertTests : BunitContext
{
    [SetUp]
    public void SetUp()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private IRenderedComponent<AiErrorAlert> RenderAlert(string code, string message) =>
        Render<AiErrorAlert>(p => p.Add(x => x.Error, new ApiError(code, message)));

    [TestCase("ai_key_required")]
    [TestCase("ai_key_rejected")]
    public void AKeyRefusal_IsAWarning_ThatLinksToProfile(string code)
    {
        var cut = RenderAlert(code, "This world needs an AI key. Add one under Your account, AI.");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".mud-alert").ClassList, Does.Contain("mud-alert-text-warning"));
            Assert.That(cut.Find("a").GetAttribute("href"), Is.EqualTo("/profile?tab=ai"));
        });
    }

    [Test]
    public void OutOfCredit_LinksToTheNamedProvidersBilling()
    {
        var cut = RenderAlert("ai_credit_exhausted",
            "The OpenAI account behind this world's key is out of credit. Add credit there, then try again.");

        var link = cut.Find("a");
        Assert.Multiple(() =>
        {
            Assert.That(link.GetAttribute("href"), Is.EqualTo("https://platform.openai.com/settings/organization/billing"));
            Assert.That(link.TextContent, Is.EqualTo("Add credit at OpenAI"));
        });
    }

    [Test]
    public void OutOfCredit_AtACompatibleEndpoint_FallsBackToProfile()
    {
        var cut = RenderAlert("ai_credit_exhausted",
            "The OpenAI-compatible account behind this world's key is out of credit. Add credit there, then try again.");

        Assert.That(cut.Find("a").GetAttribute("href"), Is.EqualTo("/profile?tab=ai"));
    }

    [Test]
    public void AnyOtherError_IsAnError_WithNoLink()
    {
        var cut = RenderAlert("ai_unavailable", "The Loremaster couldn't answer just now.");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".mud-alert").ClassList, Does.Contain("mud-alert-text-error"));
            Assert.That(cut.FindAll("a"), Is.Empty);
        });
    }
}
