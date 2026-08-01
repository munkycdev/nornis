using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using OpenAI.Chat;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The map extraction client's two-pass flow: the whole-map read, then per-tile
/// position refinement — which is strictly best-effort and sums token usage.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class AzureOpenAiMapExtractionClientTests
{
    private ChatClient _mockChatClient = null!;
    private AzureOpenAiMapExtractionClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _mockChatClient = Substitute.For<ChatClient>();
        _client = new AzureOpenAiMapExtractionClient(
            _mockChatClient, NullLogger<AzureOpenAiMapExtractionClient>.Instance);
    }

    private static byte[] TinyPng()
    {
        using var image = new Image<Rgba32>(30, 30);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private static MapExtractionRequest Request(bool refine) => new()
    {
        ImageBytes = TinyPng(),
        MediaType = "image/png",
        SourceTitle = "Realm map",
        ExistingLocations = [],
        Model = "gpt-test",
        TimeoutSeconds = 30,
        RefinePositions = refine
    };

    private static ClientResult<ChatCompletion> Completion(string json)
    {
        var completion = OpenAIChatModelFactory.ChatCompletion(
            id: "chatcmpl-test",
            finishReason: ChatFinishReason.Stop,
            content: new ChatMessageContent(json),
            model: "gpt-test",
            usage: OpenAIChatModelFactory.ChatTokenUsage(
                outputTokenCount: 10, inputTokenCount: 100, totalTokenCount: 110));
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(200);
        return ClientResult.FromValue(completion, response);
    }

    // Pass 1: one place at (0.30, 0.30) → tile 0 of the 3×3 grid.
    private const string Pass1Json = """
        {"places":[{"name":"Thistle Hold","kind":"town","x":0.30,"y":0.30,"confidence":0.9,"existingArtifactId":null}]}
        """;

    /// <summary>Queues chat responses. The ClientResults are built BEFORE the Returns call —
    /// building them inline would consume NSubstitute's last-call context (they contain a
    /// nested substitute) and break the setup.</summary>
    private void SetupResponses(params string[] jsons)
    {
        var results = jsons.Select(Completion).ToArray();
        _mockChatClient.CompleteChatAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatCompletionOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(results[0]), results.Skip(1).Select(Task.FromResult).ToArray());
    }

    [Test]
    public async Task Extract_WithRefinement_MapsTilePositionBackToFullImage()
    {
        const string refinedJson = """
            {"places":[{"name":"Thistle Hold","found":true,"x":0.5,"y":0.5}]}
            """;
        SetupResponses(Pass1Json, refinedJson);

        var result = await _client.ExtractAsync(Request(refine: true), CancellationToken.None);

        Assert.That(result.Places, Has.Count.EqualTo(1));
        // Tile 0 spans 0..(1/3 + 0.10); crop-center 0.5 maps to half that span.
        var expected = (1m / 3 + MapRefinement.Margin) / 2;
        Assert.That(result.Places[0].X, Is.EqualTo(expected).Within(0.001m));
        Assert.That(result.Places[0].Y, Is.EqualTo(expected).Within(0.001m));
        // Usage sums across both calls.
        Assert.That(result.Usage.InputTokens, Is.EqualTo(200));
        Assert.That(result.Usage.OutputTokens, Is.EqualTo(20));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(220));
        await _mockChatClient.Received(2).CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatCompletionOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Extract_RefinementDisabled_SingleCallKeepsFirstPassPositions()
    {
        SetupResponses(Pass1Json);

        var result = await _client.ExtractAsync(Request(refine: false), CancellationToken.None);

        Assert.That(result.Places[0].X, Is.EqualTo(0.30m));
        Assert.That(result.Places[0].Y, Is.EqualTo(0.30m));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(110));
        await _mockChatClient.Received(1).CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatCompletionOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Extract_RefinementCallFails_KeepsFirstPassPositions()
    {
        var firstResult = Completion(Pass1Json);
        _mockChatClient.CompleteChatAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatCompletionOptions>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(firstResult),
                _ => throw new InvalidOperationException("refinement boom"));

        var result = await _client.ExtractAsync(Request(refine: true), CancellationToken.None);

        Assert.That(result.Places[0].X, Is.EqualTo(0.30m));
        Assert.That(result.Places[0].Y, Is.EqualTo(0.30m));
    }

    [Test]
    public async Task Extract_RefinementNotFound_KeepsFirstPassPosition()
    {
        const string notFoundJson = """
            {"places":[{"name":"Thistle Hold","found":false,"x":0.5,"y":0.5}]}
            """;
        SetupResponses(Pass1Json, notFoundJson);

        var result = await _client.ExtractAsync(Request(refine: true), CancellationToken.None);

        Assert.That(result.Places[0].X, Is.EqualTo(0.30m));
        Assert.That(result.Places[0].Y, Is.EqualTo(0.30m));
    }

    [Test]
    public async Task Extract_RefinementNameMismatch_KeepsFirstPassPosition()
    {
        const string mismatchJson = """
            {"places":[{"name":"Somewhere Else","found":true,"x":0.5,"y":0.5}]}
            """;
        SetupResponses(Pass1Json, mismatchJson);

        var result = await _client.ExtractAsync(Request(refine: true), CancellationToken.None);

        Assert.That(result.Places[0].X, Is.EqualTo(0.30m));
    }

    [Test]
    public async Task Extract_NoPlaces_SkipsRefinementEntirely()
    {
        SetupResponses("""{"places":[]}""");

        var result = await _client.ExtractAsync(Request(refine: true), CancellationToken.None);

        Assert.That(result.Places, Is.Empty);
        await _mockChatClient.Received(1).CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatCompletionOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Extract_FirstPassMalformed_ThrowsParseException()
    {
        SetupResponses("not json");

        Assert.ThrowsAsync<AiExtractionParseException>(
            () => _client.ExtractAsync(Request(refine: true), CancellationToken.None));
    }
}
