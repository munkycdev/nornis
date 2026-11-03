using System.Net;
using System.Text;
using System.Text.Json;
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
/// Profile's AI keys card (feature 39): what a save sends (only the lines the GM ticked, no key
/// on "Check again"), and that a refused key reads as something to fix rather than a fault.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AiKeysCardTests : BunitContext
{
    private const string StoredOpenAiKey = """
        {"provider":"OpenAi","label":"Kelda's OpenAI","lastFour":"x9Qa","baseUrl":null,
         "addedAt":"2026-11-03T09:00:00+00:00","updatedAt":"2026-11-03T09:00:00+00:00",
         "checkedAt":"2026-11-03T09:00:00+00:00","checkFailed":false,"models":[]}
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

    private IRenderedComponent<AiKeysCard> RenderCard()
    {
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<AiKeysCard>();
    }

    private static void ClickButton(IRenderedComponent<AiKeysCard> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    [Test]
    public void AStoredKey_ShowsItsNameProviderAndLastFour_AndNothingMore()
    {
        _handler.Keys = $"[{StoredOpenAiKey}]";

        var cut = RenderCard();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-testid='ai-key-row']"), Has.Count.EqualTo(1)));
        var row = cut.Find("[data-testid='ai-key-row']").TextContent;
        Assert.Multiple(() =>
        {
            Assert.That(row, Does.Contain("Kelda's OpenAI"));
            Assert.That(row, Does.Contain("OpenAI · ending x9Qa"));
            Assert.That(cut.FindAll("button").Select(b => b.TextContent), Has.None.Contains("Add an OpenAI key"),
                "one key per provider: the stored key is replaced, not added to");
        });
    }

    [Test]
    public void AddingAnOpenAiKey_SendsTheKeyAndNoModelLines()
    {
        var cut = RenderCard();
        cut.WaitForAssertion(() => ClickButton(cut, "Add an OpenAI key"));

        var inputs = cut.FindAll("[data-testid='ai-key-editor'] input");
        inputs[0].Change("sk-proj-abcdefgh1234");
        inputs[1].Change("My OpenAI");
        ClickButton(cut, "Check and save");

        cut.WaitForAssertion(() => Assert.That(_handler.PutBody, Is.Not.Null));
        var body = JsonDocument.Parse(_handler.PutBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(_handler.PutPath, Is.EqualTo("/api/me/ai-keys/OpenAi"));
            Assert.That(body.GetProperty("key").GetString(), Is.EqualTo("sk-proj-abcdefgh1234"));
            Assert.That(body.GetProperty("label").GetString(), Is.EqualTo("My OpenAI"));
            Assert.That(body.GetProperty("baseUrl").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("models").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-testid='ai-key-editor']"), Is.Empty, "the editor closes once saved"));
    }

    [Test]
    public void ACompatibleEndpoint_SendsOnlyTheTasksTicked()
    {
        var cut = RenderCard();
        cut.WaitForAssertion(() => ClickButton(cut, "Add an OpenAI-compatible endpoint"));

        var editor = () => cut.Find("[data-testid='ai-key-editor']");
        var fields = editor().QuerySelectorAll("input:not([type='checkbox'])");
        fields[0].Change("hf_abcdefgh1234");
        fields[2].Change("https://llm.example.com/v1/");

        // Tick Ask, the first line, and name its model.
        editor().QuerySelectorAll(".nornis-ai-key-line")[0].QuerySelector("input[type='checkbox']")!.Change(true);
        editor().QuerySelectorAll(".nornis-ai-key-line")[0].QuerySelectorAll("input:not([type='checkbox'])")[0].Change("llama-4");
        ClickButton(cut, "Check and save");

        cut.WaitForAssertion(() => Assert.That(_handler.PutBody, Is.Not.Null));
        var body = JsonDocument.Parse(_handler.PutBody!).RootElement;
        var models = body.GetProperty("models").EnumerateArray().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(_handler.PutPath, Is.EqualTo("/api/me/ai-keys/OpenAiCompatible"));
            Assert.That(body.GetProperty("baseUrl").GetString(), Is.EqualTo("https://llm.example.com/v1/"));
            Assert.That(models.Select(m => (m.GetProperty("task").GetString(), m.GetProperty("modelName").GetString())),
                Is.EqualTo([("Ask", "llama-4")]));
        });
    }

    [Test]
    public void CheckAgain_SendsNoKey_SoTheStoredOneIsChecked()
    {
        _handler.Keys = $"[{StoredOpenAiKey}]";
        var cut = RenderCard();

        cut.WaitForAssertion(() => ClickButton(cut, "Check again"));

        cut.WaitForAssertion(() => Assert.That(_handler.PutBody, Is.Not.Null));
        Assert.That(JsonDocument.Parse(_handler.PutBody!).RootElement.GetProperty("key").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void ARefusedKey_ShowsTheApisMessage_AndKeepsTheEditorOpen()
    {
        _handler.SaveRefusal = """{"code":"ai_key_check_failed","message":"OpenAI didn't accept that key."}""";
        var cut = RenderCard();
        cut.WaitForAssertion(() => ClickButton(cut, "Add an OpenAI key"));

        cut.FindAll("[data-testid='ai-key-editor'] input")[0].Change("sk-proj-wrongwrong");
        ClickButton(cut, "Check and save");

        cut.WaitForAssertion(() => Assert.That(cut.Find(".mud-alert").TextContent, Does.Contain("OpenAI didn't accept that key.")));
        Assert.That(cut.FindAll("[data-testid='ai-key-editor']"), Has.Count.EqualTo(1));
    }

    [Test]
    public void TheTrialLine_ShowsWhatIsLeft_WhileTheTrialIsInUse()
    {
        _handler.Trial = """{"inUse":true,"allowanceUsd":2.00,"spentUsd":0.60,"remainingUsd":1.40,"available":true}""";

        var cut = RenderCard();

        cut.WaitForAssertion(() => Assert.That(cut.Find("[data-testid='ai-trial']").TextContent, Does.Contain("Trial: $1.40 of $2.00 left")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Keys { get; set; } = "[]";

        public string Trial { get; set; } = """{"inUse":false,"allowanceUsd":2.00,"spentUsd":0,"remainingUsd":2.00,"available":true}""";

        public string? SaveRefusal { get; set; }

        public string? PutBody { get; private set; }

        public string? PutPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                PutPath = path;
                PutBody = await request.Content!.ReadAsStringAsync(ct);
                return SaveRefusal is { } refusal
                    ? Json(HttpStatusCode.BadRequest, refusal)
                    : Json(HttpStatusCode.OK, $$"""{"key":{{StoredOpenAiKey}},"sourcesQueued":0}""");
            }

            return path switch
            {
                "/api/me/ai-keys" => Json(HttpStatusCode.OK, Keys),
                "/api/me/ai-trial" => Json(HttpStatusCode.OK, Trial),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
