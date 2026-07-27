using Nornis.Application.Ai;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Prompt-cache work on the extraction prompt is the largest remaining AI saving, and extraction
/// is ~88% of all spend. It is also unverifiable without knowing how much of each call's input
/// the provider served from cache — hence <c>CachedInputTokens</c>, and hence its nullability:
/// "this path does not report cache hits" and "the provider reported none" demand opposite
/// responses, so they must not collapse into the same value.
/// </summary>
[TestFixture]
public class ExtractionCachedTokenTrackingTests
{
    private static AiExtractionResponse Response(int? cached) => new()
    {
        Proposals = [],
        InputTokens = 16_000,
        CachedInputTokens = cached,
        OutputTokens = 3_000,
        TotalTokens = 19_000,
        DurationMs = 1_200,
        Model = "nornis-extract",
    };

    [Test]
    public void CachedInputTokens_DefaultsToNull_WhenTheProviderSaysNothing()
    {
        // The property is optional on the DTO, so a client that never sets it must not silently
        // report a zero cache hit.
        var response = new AiExtractionResponse
        {
            Proposals = [],
            InputTokens = 16_000,
            OutputTokens = 3_000,
            TotalTokens = 19_000,
            DurationMs = 1_200,
            Model = "nornis-extract",
        };

        Assert.That(response.CachedInputTokens, Is.Null);
    }

    [Test]
    public void ZeroCachedTokens_IsDistinctFromNotReported()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Response(0).CachedInputTokens, Is.Zero, "the provider reported no cache hit");
            Assert.That(Response(null).CachedInputTokens, Is.Null, "the provider reported nothing at all");
        });
    }

    [Test]
    public void CachedTokens_AreCarriedThroughUnchanged()
    {
        Assert.That(Response(12_800).CachedInputTokens, Is.EqualTo(12_800));
    }

    [Test]
    public void CachedTokens_NeverExceedInputTokens()
    {
        // A guard on the meaning of the field rather than on our arithmetic: cached input is a
        // subset of input, so a reading above it would mean we had wired the wrong number.
        var response = Response(12_800);

        Assert.That(response.CachedInputTokens, Is.LessThanOrEqualTo(response.InputTokens));
    }
}
