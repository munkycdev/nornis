using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// Profile's Models card (feature 39, Dave's picker decision): the reviewed models first, then
/// what the key's provider lists beyond them, under its own heading.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AiModelsCardTests : BunitContext
{
    private const string Models = """
        [{"task":"Ask","chosenId":null,"defaultId":"openai/gpt-5.4","options":[
           {"id":"openai/gpt-5.4","name":"gpt-5.4","provider":"OpenAi","supportsImages":true,"hasKey":true,"tested":true},
           {"id":"compatible:7a1d1c43-3d0e-4ad0-9c0c-1b5a37a4f3a1","name":"llama-4","provider":"OpenAiCompatible","supportsImages":false,"hasKey":true,"tested":false},
           {"id":"untested:OpenAi/gpt-4.1-mini","name":"gpt-4.1-mini","provider":"OpenAi","supportsImages":false,"hasKey":true,"tested":false}]}]
        """;

    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, new ViewAsState(), new ActivitySignal(),
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    [Test]
    public void TheKeysOtherModels_FollowTheReviewedOnes_UnderTheirOwnHeading()
    {
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<AiModelsCard>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-testid='ai-model-row']"), Has.Count.EqualTo(1)));

        cut.Find(".mud-select .mud-input-control").MouseDown();

        popovers.WaitForAssertion(() => Assert.That(popovers.FindAll(".mud-list-item"), Is.Not.Empty));
        var items = popovers.FindAll(".mud-list-item").Select(i => i.TextContent.Trim()).ToList();
        Assert.That(items, Is.EqualTo(
        [
            "Default: gpt-5.4",
            "gpt-5.4",
            "llama-4 (not tested by Nornis)",
            "More from your key (not tested by Nornis)",
            "gpt-4.1-mini · OpenAI",
        ]));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/api/me/ai-models"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Models, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
