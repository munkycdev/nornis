using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// A Copy button says what happened (3.38). It used to say "copied" whatever the browser did,
/// and a refusal was an unhandled exception that ended the session. The invite card stands in
/// for the three that share <see cref="JsRuntimeExtensions.TryCopyTextAsync"/>.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CopyLinkTests : BunitContext
{
    [SetUp]
    public void SetUp()
    {
        var viewAs = new ViewAsState();
        var api = new NornisApiClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost") }, viewAs, new ActivitySignal(),
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<InviteLinksCard>> RenderAsync()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        var cut = Render<InviteLinksCard>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("invite/harbor-code")));
        return cut;
    }

    private IEnumerable<string> Said() =>
        Services.GetRequiredService<ISnackbar>().ShownSnackbars.Select(s => s.Message?.ToString() ?? string.Empty);

    [Test]
    public async Task ACopyThatWorks_SaysCopied()
    {
        var cut = await RenderAsync();

        await cut.Find("button[aria-label='Copy invite link']").ClickAsync(new());

        Assert.That(Said(), Is.EqualTo((string[])["Invite link copied."]));
    }

    [Test]
    public async Task ACopyTheBrowserRefuses_SaysSo_AndTheCardIsStillThere()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
            .SetException(new JSException("NotAllowedError: Write permission denied."));
        var cut = await RenderAsync();

        await cut.Find("button[aria-label='Copy invite link']").ClickAsync(new());

        Assert.Multiple(() =>
        {
            Assert.That(Said(), Is.EqualTo((string[])[DisplayText.CopyRefused]));
            Assert.That(cut.Markup, Does.Contain("invite/harbor-code"), "the link is on screen to select by hand");
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Guid _worldId = Guid.NewGuid();

        private string World => $$"""
            {"id":"{{_worldId}}","name":"Black Harbor","description":"A port.","gameSystem":null,"myRole":"GM",
             "publicSlug":null,"publicAccessEnabled":false}
            """;

        private string Invite => $$"""
            {"id":"{{Guid.NewGuid()}}","worldId":"{{_worldId}}","code":"harbor-code","role":"Player","status":"Active",
             "useCount":0,"maxUses":null,"expiresAt":null,"createdAt":"2026-11-01T00:00:00+00:00","revokedAt":null}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/api/worlds" => Json($"[{World}]"),
                _ when path.EndsWith("/invites", StringComparison.Ordinal) => Json($"[{Invite}]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
