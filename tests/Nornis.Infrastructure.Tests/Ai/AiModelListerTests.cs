using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// "More from your key" (feature 39): which of a provider's models Profile offers, and that a
/// key's provider is asked once an hour, not on every visit.
/// </summary>
[TestFixture]
public class AiModelListerTests
{
    private const string Secret = "sk-proj-lister-canary-1234";

    private static UserAiKey Key(AiProvider provider) => new()
    {
        Id = Guid.NewGuid(),
        Provider = provider,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static AiModelLister Lister(RecordingHandler handler, IMemoryCache? cache = null) =>
        new(new HttpClient(handler), cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<AiModelLister>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [TestCase("gpt-4.1-mini", true)]
    [TestCase("gpt-5.4", true)]
    [TestCase("o3", true)]
    [TestCase("o4-mini", true)]
    [TestCase("chatgpt-4o-latest", true)]
    [TestCase("gpt-image-1", false)]
    [TestCase("gpt-4o-mini-tts", false)]
    [TestCase("gpt-4o-transcribe", false)]
    [TestCase("gpt-4o-realtime-preview", false)]
    [TestCase("gpt-4o-audio-preview", false)]
    [TestCase("gpt-4o-search-preview", false)]
    [TestCase("gpt-3.5-turbo-instruct", false)]
    [TestCase("gpt-5-codex", false)]
    [TestCase("o3-pro", false)]
    [TestCase("o3-deep-research", false)]
    [TestCase("text-embedding-3-large", false)]
    [TestCase("omni-moderation-latest", false)]
    [TestCase("dall-e-3", false)]
    [TestCase("whisper-1", false)]
    [TestCase("davinci-002", false)]
    public void OpenAis_list_is_cut_to_models_that_answer_chat(string id, bool chat) =>
        Assert.That(AiModelLister.IsChatModel(AiProvider.OpenAi, id), Is.EqualTo(chat));

    [Test]
    public async Task OpenAis_answer_becomes_its_chat_models_sorted()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"object":"list","data":[{"id":"gpt-image-1","object":"model"},{"id":"o4-mini","object":"model"},
             {"id":"gpt-4.1-mini","object":"model"},{"id":"gpt-4.1-mini","object":"model"}]}
            """));

        var models = await Lister(handler).ListChatModelsAsync(Key(AiProvider.OpenAi), Secret, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models!.Select(m => m.Id), Is.EqualTo(["gpt-4.1-mini", "o4-mini"]));
            Assert.That(models!.Select(m => m.Name), Is.EqualTo(["gpt-4.1-mini", "o4-mini"]), "OpenAI gives no display name");
            Assert.That(handler.Uris.Single(), Is.EqualTo(new Uri("https://api.openai.com/v1/models")));
            Assert.That(handler.Authorizations.Single(), Is.EqualTo("Bearer " + Secret));
        });
    }

    [Test]
    public async Task Anthropics_answer_keeps_its_display_names_and_is_asked_for_one_whole_page()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"data":[{"type":"model","id":"claude-haiku-4-5","display_name":"Claude Haiku 4.5","created_at":"2025-10-01T00:00:00Z"}],
             "has_more":false,"first_id":"claude-haiku-4-5","last_id":"claude-haiku-4-5"}
            """));

        var models = await Lister(handler).ListChatModelsAsync(Key(AiProvider.Anthropic), Secret, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models!.Single(), Is.EqualTo(new Nornis.Application.Ai.AiListedModel("claude-haiku-4-5", "Claude Haiku 4.5")));
            Assert.That(handler.Uris.Single(), Is.EqualTo(new Uri("https://api.anthropic.com/v1/models?limit=1000")));
            Assert.That(handler.ApiKeys.Single(), Is.EqualTo(Secret));
        });
    }

    [Test]
    public async Task Geminis_answer_keeps_chat_models_that_generate_content_without_the_models_prefix()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"models":[
              {"name":"models/gemini-3.8-flash","displayName":"Gemini 3.8 Flash","supportedGenerationMethods":["generateContent","countTokens"]},
              {"name":"models/gemini-3.8-flash-live","displayName":"Gemini 3.8 Flash Live","supportedGenerationMethods":["bidiGenerateContent"]},
              {"name":"models/gemini-3.8-flash-tts","displayName":"Gemini 3.8 Flash TTS","supportedGenerationMethods":["generateContent"]},
              {"name":"models/gemini-embedding-2","displayName":"Gemini Embedding","supportedGenerationMethods":["embedContent"]},
              {"name":"models/gemma-4-27b","displayName":"Gemma 4","supportedGenerationMethods":["generateContent"]},
              {"name":"models/gemini-3.5-flash-lite","supportedGenerationMethods":["generateContent"]}
            ]}
            """));

        var models = await Lister(handler).ListChatModelsAsync(Key(AiProvider.Gemini), Secret, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models, Is.EqualTo(
            [
                new Nornis.Application.Ai.AiListedModel("gemini-3.5-flash-lite", "gemini-3.5-flash-lite"),
                new Nornis.Application.Ai.AiListedModel("gemini-3.8-flash", "Gemini 3.8 Flash"),
            ]));
            Assert.That(handler.Uris.Single(), Is.EqualTo(new Uri("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000")));
            Assert.That(handler.GoogleKeys.Single(), Is.EqualTo(Secret));
        });
    }

    [Test]
    public async Task An_answer_is_kept_an_hour_per_key_version_and_a_replaced_key_is_asked_afresh()
    {
        var handler = new RecordingHandler(_ => Json("""{"data":[{"id":"gpt-4.1-mini"}]}"""));
        var lister = Lister(handler);
        var key = Key(AiProvider.OpenAi);

        await lister.ListChatModelsAsync(key, Secret, CancellationToken.None);
        await lister.ListChatModelsAsync(key, Secret, CancellationToken.None);
        key.UpdatedAt = key.UpdatedAt.AddMinutes(1);
        await lister.ListChatModelsAsync(key, Secret, CancellationToken.None);

        Assert.That(handler.Uris, Has.Count.EqualTo(2));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task A_failed_answer_is_null_and_not_kept(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var lister = Lister(handler);
        var key = Key(AiProvider.OpenAi);

        var first = await lister.ListChatModelsAsync(key, Secret, CancellationToken.None);
        await lister.ListChatModelsAsync(key, Secret, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Null);
            Assert.That(handler.Uris, Has.Count.EqualTo(2), "the next visit asks again");
        });
    }

    [Test]
    public async Task A_network_failure_or_a_garbled_answer_is_null()
    {
        var unreachable = await Lister(new RecordingHandler(_ => throw new HttpRequestException("no route")))
            .ListChatModelsAsync(Key(AiProvider.OpenAi), Secret, CancellationToken.None);
        var garbled = await Lister(new RecordingHandler(_ => Json("<html>")))
            .ListChatModelsAsync(Key(AiProvider.OpenAi), Secret, CancellationToken.None);

        Assert.That((unreachable, garbled), Is.EqualTo((null as object, null as object)));
    }

    [TestCase(AiProvider.OpenAiCompatible)]
    [TestCase(AiProvider.AzureOpenAi)]
    public async Task Providers_without_a_list_here_are_not_asked(AiProvider provider)
    {
        var handler = new RecordingHandler(_ => Json("""{"data":[]}"""));

        var models = await Lister(handler).ListChatModelsAsync(Key(provider), Secret, CancellationToken.None);

        Assert.That((models, handler.Uris.Count), Is.EqualTo((null as object, 0)));
    }

    [Test]
    public void An_answer_without_a_data_array_lists_nothing() =>
        Assert.That(AiModelLister.Parse(AiProvider.OpenAi, JsonDocument.Parse("""{"error":"nope"}""").RootElement), Is.Empty);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];

        public List<string?> Authorizations { get; } = [];

        public List<string?> ApiKeys { get; } = [];

        public List<string?> GoogleKeys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            ApiKeys.Add(request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : null);
            GoogleKeys.Add(request.Headers.TryGetValues("x-goog-api-key", out var googleKeys) ? googleKeys.Single() : null);
            return Task.FromResult(respond(request));
        }
    }
}
