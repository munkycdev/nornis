using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using SkiaSharp;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The map extraction client's two-pass flow: the whole-map read, then per-tile
/// position refinement — which is strictly best-effort and sums token usage.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class AzureOpenAiMapExtractionClientTests
{
    private FakeChatClient _chatClient = null!;
    private AzureOpenAiMapExtractionClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _chatClient = new FakeChatClient();
        _client = new AzureOpenAiMapExtractionClient(
            _chatClient, NullLogger<AzureOpenAiMapExtractionClient>.Instance);
    }

    private static byte[] TinyPng()
    {
        using var bitmap = new SKBitmap(30, 30);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
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

    // Pass 1: one place at (0.30, 0.30) → tile 0 of the 3×3 grid.
    private const string Pass1Json = """
        {"places":[{"name":"Thistle Hold","kind":"town","x":0.30,"y":0.30,"confidence":0.9,"existingArtifactId":null}]}
        """;

    /// <summary>Queues one chat response per JSON document, each reporting 100 in / 10 out.</summary>
    private void SetupResponses(params string[] jsons)
    {
        foreach (var json in jsons)
        {
            _chatClient.Returns(json, input: 100, output: 10, total: 110);
        }
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
        Assert.That(_chatClient.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Extract_RefinementDisabled_SingleCallKeepsFirstPassPositions()
    {
        SetupResponses(Pass1Json);

        var result = await _client.ExtractAsync(Request(refine: false), CancellationToken.None);

        Assert.That(result.Places[0].X, Is.EqualTo(0.30m));
        Assert.That(result.Places[0].Y, Is.EqualTo(0.30m));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(110));
        Assert.That(_chatClient.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Extract_RefinementCallFails_KeepsFirstPassPositions()
    {
        _chatClient
            .Returns(Pass1Json, input: 100, output: 10, total: 110)
            .Throws(new InvalidOperationException("refinement boom"));

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
        Assert.That(_chatClient.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void Extract_FirstPassMalformed_ThrowsParseException()
    {
        SetupResponses("not json");

        Assert.ThrowsAsync<AiParseException>(
            () => _client.ExtractAsync(Request(refine: true), CancellationToken.None));
    }
}
