using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The live half of <see cref="WebResearchWireTests"/> (feature 40): one research turn per
/// provider, through the client a GM's key gets and the executor Prep calls, on the shipped
/// default model. It checks what the fakes can't: that the provider takes the tool the wire tests
/// pin, and that the stream really reports pages and searches the way the readers expect.
/// Explicit, so CI and a plain <c>dotnet test</c> never run it. Each provider is skipped without
/// its key, and each run costs a few cents.
///
/// <code>
/// $env:NORNIS_OPENAI_KEY = "sk-…"; $env:NORNIS_ANTHROPIC_KEY = "sk-ant-…"; $env:NORNIS_GEMINI_KEY = "AIza…"
/// dotnet test --project tests/Nornis.Infrastructure.Tests --filter "FullyQualifiedName~LiveWebResearchTests" --output detailed
/// </code>
/// </summary>
[TestFixture]
[Explicit("Calls OpenAI, Anthropic and Gemini on real keys")]
[Category("Live")]
public class LiveWebResearchTests
{
    private static readonly AiCallOptions Research = new(MaxOutputTokens: 6_000, WebSearch: new AiWebSearch(MaxSearches: 3));

    [TestCase(AiProvider.OpenAi, "NORNIS_OPENAI_KEY", "gpt-5.4")]
    [TestCase(AiProvider.Anthropic, "NORNIS_ANTHROPIC_KEY", "claude-sonnet-5-5")]
    [TestCase(AiProvider.Gemini, "NORNIS_GEMINI_KEY", "gemini-3.8-flash")]
    public async Task A_research_turn_searches_and_reports_its_pages(AiProvider provider, string variable, string model)
    {
        var secret = Environment.GetEnvironmentVariable(variable) ?? "";
        if (secret.Length == 0)
        {
            Assert.Ignore($"Set {variable} to run the live check for {provider}.");
        }

        using var clients = new UserKeyChatClients(maxRetries: 1);
        var key = new UserAiKey { Id = Guid.NewGuid(), Provider = provider, UpdatedAt = DateTimeOffset.UtcNow };
        var shape = provider == AiProvider.OpenAi ? AiClientShape.Responses : AiClientShape.Chat;
        IAiProviderTraits traits = provider switch
        {
            AiProvider.OpenAi => OpenAiProviderTraits.Instance,
            AiProvider.Anthropic => AnthropicProviderTraits.Instance,
            _ => GeminiProviderTraits.Instance,
        };
        var binding = new AiBinding(clients.For(key, model, () => secret, shape), traits,
            new AiFunding(provider, model, AiFundedBy.Key, key.Id), key.UpdatedAt);

        var request = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(Guid.NewGuid()),
            SystemPrompt = "You help a game master. Search the web to answer, in two sentences.",
            UserMessage = "What are the wards of the city of Waterdeep in the Forgotten Realms?",
            Model = model,
            TimeoutSeconds = 180,
        };

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in AiCallExecutor.StreamAsync(
            new OneBinder(binding), AiTask.Prep, request, Research, "LiveWebResearch", NullLogger.Instance, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var last = chunks[^1];
        TestContext.Out.WriteLine(string.Concat(chunks.Select(c => c.Text)));
        TestContext.Out.WriteLine($"searches: {last.WebResearch?.Searches}; usage searches: {last.Usage?.WebSearches}");
        foreach (var page in last.WebResearch?.Pages ?? [])
        {
            TestContext.Out.WriteLine($"  {page.Title} <{page.Url}>");
        }

        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.Not.Empty);
            Assert.That(last.WebResearch, Is.Not.Null);
            Assert.That(last.WebResearch!.Searches, Is.GreaterThan(0), "the provider says it searched");
            Assert.That(last.Usage?.WebSearches, Is.EqualTo(last.WebResearch.Searches), "the ledger prices what was read");
            Assert.That(last.WebResearch.Pages, Is.Not.Empty, "the answer drew on at least one page");
        });
    }

    private sealed class OneBinder(AiBinding binding) : IAiCallBinder
    {
        public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, bool needsWebSearch, CancellationToken ct) =>
            Task.FromResult(binding);

        public Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct) => Task.CompletedTask;
    }
}
