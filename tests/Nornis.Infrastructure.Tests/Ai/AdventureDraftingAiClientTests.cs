using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Prep's drafting transport (feature 29): what it sends, and the two things it reports that no
/// other client needs, a cut-off at the output ceiling and the reasoning tokens spent under it.
/// </summary>
[TestFixture]
public class AdventureDraftingAiClientTests
{
    private FakeChatClient _chatClient = null!;
    private AdventureDraftingAiClient _client = null!;

    private static readonly AiPromptRequest Request = new()
    {
        Scope = AiCallScope.ForWorld(Guid.NewGuid()),
        SystemPrompt = "You are helping a game master prepare an adventure.",
        UserMessage = "## The section to write: [Scene] The Docks",
        Model = "nornis-ask",
        TimeoutSeconds = 30,
    };

    [SetUp]
    public void SetUp()
    {
        _chatClient = new FakeChatClient();
        _client = new AdventureDraftingAiClient(new FakeAiCallBinder(_chatClient), NullLogger<AdventureDraftingAiClient>.Instance);
    }

    private async Task<List<AiStreamChunk>> DrainAsync()
    {
        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in _client.StreamAsync(Request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Test]
    public void OnlyAResearchTurn_AsksForTheSearchTool_WithItsOwnCeiling()
    {
        var research = new AiPromptRequest
        {
            Scope = Request.Scope,
            SystemPrompt = Request.SystemPrompt,
            UserMessage = Request.UserMessage,
            Model = Request.Model,
            TimeoutSeconds = 180,
            WebSearch = new AiWebSearchRequest(MaxSearches: 5, OutputCeilingTokens: 6_000),
        };

        Assert.Multiple(() =>
        {
            Assert.That(AdventureDraftingAiClient.OptionsFor(Request), Is.SameAs(AdventureDraftingAiClient.CallOptions),
                "a turn without the switch is sent exactly as before");
            Assert.That(AdventureDraftingAiClient.OptionsFor(research),
                Is.EqualTo(new AiCallOptions(MaxOutputTokens: 6_000, WebSearch: new AiWebSearch(5))));
        });
    }

    [Test]
    public void ADesignTurn_CarriesItsOwnCeiling_OnItsOwnOrWithResearch()
    {
        var design = new AiPromptRequest
        {
            Scope = Request.Scope,
            SystemPrompt = Request.SystemPrompt,
            UserMessage = Request.UserMessage,
            Model = Request.Model,
            TimeoutSeconds = 220,
            OutputCeilingTokens = 16_000,
        };
        var researching = new AiPromptRequest
        {
            Scope = Request.Scope,
            SystemPrompt = Request.SystemPrompt,
            UserMessage = Request.UserMessage,
            Model = Request.Model,
            TimeoutSeconds = 220,
            OutputCeilingTokens = 16_000,
            WebSearch = new AiWebSearchRequest(MaxSearches: 5, OutputCeilingTokens: 6_000),
        };

        Assert.Multiple(() =>
        {
            Assert.That(AdventureDraftingAiClient.OptionsFor(design), Is.EqualTo(new AiCallOptions(MaxOutputTokens: 16_000)));
            Assert.That(AdventureDraftingAiClient.OptionsFor(researching),
                Is.EqualTo(new AiCallOptions(MaxOutputTokens: 16_000, WebSearch: new AiWebSearch(5))));
        });
    }

    [Test]
    public async Task StreamAsync_SpentTheWholeCeiling_WithoutSayingWhy_IsCutOff()
    {
        // Feature 41: a provider path that loses the finish reason still reports the tokens.
        _chatClient.Streams(
            ["> Gulls wheel over the"],
            new UsageDetails { InputTokenCount = 4_000, OutputTokenCount = AdventureDraftingAiClient.OutputCeilingTokens, TotalTokenCount = 6_500 });

        var chunks = await DrainAsync();

        Assert.That(chunks[^1].CutOff, Is.True);
    }

    [Test]
    public async Task StreamAsync_SendsThePromptWithTheCeiling()
    {
        _chatClient.Streams(["> Gulls wheel over the docks."], finishReason: ChatFinishReason.Stop);

        await DrainAsync();

        var (messages, options) = _chatClient.Calls.Single();
        var raw = options?.RawRepresentationFactory?.Invoke(_chatClient) as OpenAI.Chat.ChatCompletionOptions;
        Assert.Multiple(() =>
        {
            Assert.That(messages.Select(m => m.Role), Is.EqualTo([ChatRole.System, ChatRole.User]));
            Assert.That(messages[0].Text, Is.EqualTo(Request.SystemPrompt));
            Assert.That(messages[1].Text, Is.EqualTo(Request.UserMessage));
            Assert.That(raw?.MaxOutputTokenCount, Is.EqualTo(AdventureDraftingAiClient.OutputCeilingTokens));
        });
    }

    [Test]
    public async Task StreamAsync_ACleanFinish_IsNotCutOff_AndCarriesCachedAndReasoningTokens()
    {
        _chatClient.Streams(
            ["> Gulls wheel ", "over the docks."],
            new UsageDetails
            {
                InputTokenCount = 4_000,
                CachedInputTokenCount = 3_072,
                OutputTokenCount = 600,
                ReasoningTokenCount = 0,
                TotalTokenCount = 4_600,
            },
            finishReason: ChatFinishReason.Stop);

        var chunks = await DrainAsync();

        var last = chunks[^1];
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.EqualTo("> Gulls wheel over the docks."));
            Assert.That(last.CutOff, Is.False);
            Assert.That(last.Usage!.CachedInputTokens, Is.EqualTo(3_072));
            Assert.That(last.Usage.ReasoningTokens, Is.EqualTo(0));
            Assert.That(last.Usage.Model, Is.EqualTo("nornis-ask"));
        });
    }

    [Test]
    public async Task StreamAsync_StoppedByTheCeiling_SaysCutOff()
    {
        _chatClient.Streams(
            ["> Gulls wheel over the"],
            new UsageDetails { InputTokenCount = 4_000, OutputTokenCount = 2_500, ReasoningTokenCount = 900, TotalTokenCount = 6_500 },
            finishReason: ChatFinishReason.Length);

        var chunks = await DrainAsync();

        Assert.Multiple(() =>
        {
            Assert.That(chunks[^1].CutOff, Is.True);
            Assert.That(chunks[^1].Usage!.ReasoningTokens, Is.EqualTo(900));
        });
    }

    [Test]
    public async Task StreamAsync_HttpFailure_IsTheUsualTaxonomy()
    {
        _chatClient.Streams([], thenThrow: FakeChatClient.HttpFailure(429));

        var ex = await Assert.ThrowsAsync<AiHttpException>(async () => await DrainAsync());

        Assert.That(ex!.StatusCode, Is.EqualTo(429));
    }
}
