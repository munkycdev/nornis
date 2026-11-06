using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The live half of <see cref="GeminiProviderTraitsTests"/> (feature 39 task B0): does each
/// Gemini model in the catalog stream, read an image, answer to a strict schema and report its
/// usage, through the same client a GM's key gets? Explicit, so CI and a plain
/// <c>dotnet test</c> never run it. It costs well under a cent per model, on the key in
/// <c>NORNIS_GEMINI_KEY</c>, and it checks every Gemini line of the API's appsettings.json.
///
/// <code>
/// $env:NORNIS_GEMINI_KEY = "AIza…"
/// dotnet test --project tests/Nornis.Infrastructure.Tests --filter "FullyQualifiedName~LiveGeminiConformanceTests" --output detailed
/// </code>
/// </summary>
[TestFixture]
[Explicit("Calls Google's Gemini API on a real key")]
[Category("Live")]
public class LiveGeminiConformanceTests
{
    /// <summary>A 16×16 solid red PNG.</summary>
    private const string RedSquarePng =
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGO4IyJCEmIY1TCqYfhqAAACcQQQFwFJQgAAAABJRU5ErkJggg==";

    private string _secret = "";
    private IReadOnlyList<(string Model, bool SupportsImages)> _catalog = [];
    private UserKeyChatClients _clients = null!;

    private UserAiKey Key { get; } = new() { Id = Guid.NewGuid(), Provider = AiProvider.Gemini, UpdatedAt = DateTimeOffset.UtcNow };

    [OneTimeSetUp]
    public void SetUp()
    {
        _secret = Environment.GetEnvironmentVariable("NORNIS_GEMINI_KEY") ?? "";
        if (_secret.Length == 0)
        {
            Assert.Ignore("Set NORNIS_GEMINI_KEY to a Gemini API key to run the live check.");
        }

        _catalog = CatalogGeminiModels();
        Assert.That(_catalog, Is.Not.Empty, "the API's catalog lists no Gemini model");
        _clients = new UserKeyChatClients(maxRetries: 1);
    }

    [OneTimeTearDown]
    public void TearDown() => _clients?.Dispose();

    [Test]
    public async Task Every_catalog_model_is_on_the_keys_list_of_chat_models()
    {
        var lister = new AiModelLister(new HttpClient(), new MemoryCache(new MemoryCacheOptions()), NullLogger<AiModelLister>.Instance);

        var listed = await lister.ListChatModelsAsync(Key, _secret, CancellationToken.None);

        TestContext.Out.WriteLine($"listed: {string.Join(", ", listed?.Select(m => m.Id) ?? [])}");
        Assert.That(listed?.Select(m => m.Id), Is.SupersetOf(_catalog.Select(c => c.Model)));
    }

    [Test]
    public async Task Every_catalog_model_streams_and_reports_its_usage()
    {
        foreach (var (model, _) in _catalog)
        {
            var text = new StringBuilder();
            UsageDetails? usage = null;
            await foreach (var update in Client(model).GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.System, "You are terse."), new ChatMessage(ChatRole.User, "In one sentence, describe a harbor town at dawn.")],
                GeminiProviderTraits.Instance.ChatOptions(new AiCallOptions(MaxOutputTokens: 2_000))))
            {
                text.Append(update.Text);
                usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
            }

            TestContext.Out.WriteLine($"{model}: {Describe(usage)}; \"{text}\"");
            Assert.Multiple(() =>
            {
                Assert.That(text.ToString(), Is.Not.Empty, model);
                Assert.That(usage?.InputTokenCount, Is.GreaterThan(0), $"{model} reported no input tokens");
                Assert.That(usage?.OutputTokenCount, Is.GreaterThan(0), $"{model} reported no output tokens");
            });
        }
    }

    [Test]
    public async Task Every_catalog_model_answers_to_a_strict_schema()
    {
        const string schema = """
            {"type":"object","properties":{"town":{"type":"string"},"ships":{"type":"integer"},"captain":{"type":["string","null"]}},
             "required":["town","ships","captain"],"additionalProperties":false}
            """;

        foreach (var (model, _) in _catalog)
        {
            var response = await Client(model).GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Invent a harbor town with a few ships and no named captain.")],
                GeminiProviderTraits.Instance.ChatOptions(new AiCallOptions(new AiJsonSchema("harbor", schema), MaxOutputTokens: 2_000)));

            TestContext.Out.WriteLine($"{model}: {Describe(response.Usage)}; {response.Text}");
            using var answer = JsonDocument.Parse(response.Text);
            var root = answer.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(["town", "ships", "captain"]), model);
                Assert.That(root.GetProperty("ships").ValueKind, Is.EqualTo(JsonValueKind.Number), model);
            });
        }
    }

    [Test]
    public async Task Every_catalog_model_marked_for_images_reads_one()
    {
        foreach (var (model, _) in _catalog.Where(c => c.SupportsImages))
        {
            var response = await Client(model).GetResponseAsync(
                [new ChatMessage(ChatRole.User,
                [
                    new TextContent("What single colour fills this image? Answer with one word."),
                    new DataContent(Convert.FromBase64String(RedSquarePng), "image/png"),
                ])],
                GeminiProviderTraits.Instance.ChatOptions(new AiCallOptions(MaxOutputTokens: 2_000)));

            TestContext.Out.WriteLine($"{model}: {Describe(response.Usage)}; \"{response.Text}\"");
            Assert.That(response.Text, Does.Contain("red").IgnoreCase, model);
        }
    }

    [Test]
    public async Task A_key_google_does_not_know_is_rejected_on_save_and_on_a_call()
    {
        const string bad = "AIzaSy-not-a-real-key-000000000000000";
        var checker = new AiKeyChecker(new HttpClient(), NullLogger<AiKeyChecker>.Instance);

        var outcome = await checker.CheckAsync(AiProvider.Gemini, bad, null, CancellationToken.None);
        var thrown = await Assert.CatchAsync(() => Client(_catalog[0].Model, bad).GetResponseAsync("Hello"));
        var translated = GeminiProviderTraits.Instance.TranslateFailure(
            thrown!, new AiFunding(AiProvider.Gemini, _catalog[0].Model, AiFundedBy.Key, AiKeyId: Key.Id), "Live", NullLogger.Instance);

        TestContext.Out.WriteLine($"check: {outcome}; call: {thrown!.GetType().Name}: {thrown.Message}");
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AiKeyCheckOutcome.Rejected));
            Assert.That(translated, Is.InstanceOf<AiKeyRejectedException>());
        });
    }

    private IChatClient Client(string model, string? secret = null)
    {
        var key = secret is null ? Key : new UserAiKey { Id = Guid.NewGuid(), Provider = AiProvider.Gemini, UpdatedAt = DateTimeOffset.UtcNow };
        return _clients.For(key, model, () => secret ?? _secret);
    }

    private static string Describe(UsageDetails? usage) =>
        usage is null
            ? "no usage"
            : $"input {usage.InputTokenCount} (cached {usage.CachedInputTokenCount}), output {usage.OutputTokenCount} "
              + $"(thinking {usage.ReasoningTokenCount}, already folded into output)";

    /// <summary>The Gemini lines of the API's own appsettings.json, so the check covers what ships.</summary>
    private static IReadOnlyList<(string Model, bool SupportsImages)> CatalogGeminiModels()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nornis.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "the repository root (Nornis.sln) was not found above the test binaries");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory!.FullName, "src", "Nornis.Api", "appsettings.json"))
            .Build();

        return configuration.GetSection("AiModels:Catalog").GetChildren()
            .Where(line => line["Provider"] == nameof(AiProvider.Gemini))
            .Select(line => (line["Model"]!, string.Equals(line["SupportsImages"], "true", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
