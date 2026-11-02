using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.State;
using NUnit.Framework;
using PrepPage = Nornis.Web.Components.Pages.Prep;

namespace Nornis.Web.Tests.Components;

/// <summary>The Prep list's spend line (feature 29): the month's drafting against its ceiling, for the GM only.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PrepPageTests : BunitContext
{
    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(new BreadcrumbState());
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<PrepPage>> RenderAsync()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        return Render<PrepPage>();
    }

    [Test]
    public async Task TheGm_SeesTheMonthsDraftingSpend_AgainstItsCeiling()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Drafting this month: $1.25 of $10.00")));
    }

    [Test]
    public async Task ASpentCeiling_SaysSo()
    {
        _handler.Budget = """{"spentThisMonthUsd":10.02,"monthlyBudgetUsd":10.0,"isExceeded":true}""";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("$10.02 of $10.00, spent until next month")));
    }

    [Test]
    public async Task APlayer_NeverAsksForTheSpend()
    {
        _handler.Role = "Player";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("GMs only")));
        Assert.That(_handler.Requests, Has.None.Contains("prep-budget"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Guid _worldId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public string Budget { get; set; } = """{"spentThisMonthUsd":1.25,"monthlyBudgetUsd":10.0,"isExceeded":false}""";
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            return Task.FromResult(path switch
            {
                "/api/worlds" => Json($$"""
                    [{"id":"{{_worldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"{{Role}}"}]
                    """),
                _ when path.EndsWith("/prep-budget", StringComparison.Ordinal) => Json(Budget),
                _ when path.EndsWith("/prep", StringComparison.Ordinal) || path.EndsWith("/campaigns", StringComparison.Ordinal) => Json("[]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
