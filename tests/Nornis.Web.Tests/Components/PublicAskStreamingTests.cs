using System.IO.Pipelines;
using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages.Public.World;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The public world's Ask renders the Loremaster's words as they arrive — proven on screen, not
/// just in the client: a handler that fires correctly can still leave the reader looking at a
/// spinner until the last word.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PublicAskStreamingTests : BunitContext
{
    private readonly Pipe _answer = new();

    [SetUp]
    public void SetUp()
    {
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(new StubHandler(_answer)) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    [Test]
    public async Task Ask_ShowsTheAnswerAsItIsWritten_ThenTheFinishedAnswerWithItsCitations()
    {
        var cut = Render<PublicWorld>(p => p.Add(x => x.Slug, "black-harbor"));
        // WaitForState then assert, not WaitForAssertion: NUnit 4 records every failed Assert.That
        // as a test failure even when bUnit catches it and retries.
        cut.WaitForState(() => cut.Markup.Contains("Ask the Loremaster"));

        cut.Find("input").Input("Who runs the harbor?");
        cut.Find("button[aria-label=Ask]").Click();

        await WriteAsync("""
            event: start
            data: {"confidence":"High","caveats":[]}

            event: delta
            data: {"text":"Captain Voss runs the harbor [ref:art"}


            """);

        cut.WaitForState(() => cut.Markup.Contains("Captain Voss runs the harbor"));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("nornis-answer-streaming"), "marked as still arriving");
            Assert.That(cut.Markup, Does.Not.Contain("[ref:"), "a half-written marker never shows");
            Assert.That(cut.Markup, Does.Not.Contain("Consulting the Loremaster"), "the spinner gave way to the words");
        });

        await WriteAsync("""
            event: delta
            data: {"text":"-1]."}

            event: done
            data: {"answer":"Captain Voss runs the harbor [ref:art-1].","citations":[{"referenceId":"art-1","type":"Artifact","displayName":"Captain Voss","artifactId":"11111111-1111-1111-1111-111111111111","factId":null,"relationshipId":null,"sourceId":null,"documentId":null,"slug":"captain-voss"}],"confidence":"High","caveats":[]}


            """);
        await _answer.Writer.CompleteAsync();

        cut.WaitForState(() => !cut.Markup.Contains("nornis-answer-streaming"));
        Assert.That(cut.Markup, Does.Contain("Captain Voss</a>"), "the citation links once the answer is done");
    }

    private async Task WriteAsync(string sse)
    {
        await _answer.Writer.WriteAsync(Encoding.UTF8.GetBytes(sse.ReplaceLineEndings("\n")));
        await _answer.Writer.FlushAsync();
    }

    private sealed class StubHandler(Pipe answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/public/worlds/black-harbor")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"slug":"black-harbor","name":"Black Harbor","description":null,"gameSystem":null,"askEnabled":true}""",
                        Encoding.UTF8, "application/json"),
                });
            }

            if (path == "/api/public/worlds/black-harbor/ask" && request.Method == HttpMethod.Post)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(answer.Reader.AsStream()) };
                response.Content.Headers.ContentType = new("text/event-stream");
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
