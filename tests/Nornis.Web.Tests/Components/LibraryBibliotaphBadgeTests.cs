using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Library tells a book from the world's Bibliotaph apart from an upload (feature 31): the
/// Bibliotaph book carries the badge, the upload and a catalogued upload don't.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class LibraryBibliotaphBadgeTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
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

    [Test]
    public async Task Only_the_book_from_Bibliotaph_carries_the_badge()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        var cut = Render<Library>();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-list-row"), Has.Count.EqualTo(3)));
        var rows = cut.FindAll(".nornis-list-row")
            .ToDictionary(r => r.QuerySelector(".nornis-list-title")!.TextContent.Trim(),
                r => r.QuerySelector(".nornis-bibliotaph-badge") is not null);

        Assert.That(rows, Is.EqualTo(new Dictionary<string, bool>
        {
            ["Player's Guide"] = true,
            ["Harbor map"] = false,
            ["Catalogued handout"] = false,
        }));
        Assert.That(cut.Find(".nornis-bibliotaph-badge").TextContent.Trim(), Is.EqualTo("Bibliotaph"));
    }

    private static LibraryDocumentDto Document(string title, string origin, bool inBibliotaph) => new(
        Guid.NewGuid(), WorldId, title, "file.pdf", "application/pdf", 1024, "Sourcebook", "GMOnly", "Stored",
        12, 0, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, origin, inBibliotaph);

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/worlds")
            {
                return Json($$"""
                    [{"id":"{{WorldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"GM"}]
                    """);
            }

            if (path.EndsWith("/library/bibliotaph", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(new LibraryBibliotaphDto(LibraryBibliotaphDto.Ok, "Dave"), JsonSerializerOptions.Web));
            }

            if (path.EndsWith("/library", StringComparison.Ordinal))
            {
                LibraryDocumentDto[] documents =
                [
                    Document("Player's Guide", "Bibliotaph", inBibliotaph: true),
                    Document("Harbor map", "Upload", inBibliotaph: false),
                    Document("Catalogued handout", "Upload", inBibliotaph: true),
                ];
                return Json(JsonSerializer.Serialize(documents, JsonSerializerOptions.Web));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
