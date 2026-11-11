using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// A 400 from OpenAI's image endpoint (feature 38) is read from its error body: the safety
/// system's codes are a moderation block, anything else carries OpenAI's own reason. The first
/// live Generate hit "The model 'gpt-image-1' does not exist" and was shown as a refusal.
/// </summary>
[TestFixture]
public class ImageArtRejectionTests
{
    [TestCase("moderation_blocked", "Your request was rejected as a result of our safety system.")]
    [TestCase("content_policy_violation", "Rejected.")]
    [TestCase(null, "Your request was rejected by the safety system.")]
    public void TheSafetySystemsCodes_AreModeration(string? code, string message)
    {
        var body = code is null
            ? $$$"""{"error":{"message":"{{{message}}}","type":"image_generation_user_error"}}"""
            : $$$"""{"error":{"message":"{{{message}}}","type":"image_generation_user_error","code":"{{{code}}}"}}""";
        var ex = FakeChatClient.HttpFailure(400, body);

        var rejection = OpenAiImageArtGenerator.Rejection(ex, ex);

        Assert.That(rejection.Moderation, Is.True);
    }

    [Test]
    public void AnyOther400_CarriesOpenAisReason()
    {
        var ex = FakeChatClient.HttpFailure(400,
            """{"error":{"message":"The model 'gpt-image-1' does not exist.","type":"image_generation_user_error","param":"model","code":"invalid_value"}}""");

        var rejection = OpenAiImageArtGenerator.Rejection(ex, ex);

        Assert.Multiple(() =>
        {
            Assert.That(rejection.Moderation, Is.False);
            Assert.That(rejection.Reason, Is.EqualTo("The model 'gpt-image-1' does not exist."));
            Assert.That(rejection.StatusCode, Is.EqualTo(400));
        });
    }

    [Test]
    public void ABodyThatIsntOpenAisShape_HasNoReason()
    {
        var ex = FakeChatClient.HttpFailure(400, "<html>bad gateway</html>");

        var rejection = OpenAiImageArtGenerator.Rejection(ex, ex);

        Assert.That((rejection.Moderation, rejection.Reason), Is.EqualTo((false, (string?)null)));
    }

    [Test]
    public void TheImageEndpointsBillingLimit_IsTheKeysAccountOutOfCredit()
    {
        var ex = FakeChatClient.HttpFailure(400,
            """{"error":{"message":"Billing hard limit has been reached.","type":"image_generation_user_error","code":"billing_hard_limit_reached"}}""");
        var funding = new AiFunding(AiProvider.OpenAi, "gpt-image-2.6-sunburst", AiFundedBy.Key, Guid.NewGuid(), Guid.NewGuid());

        var translated = OpenAiProviderTraits.Instance.TranslateFailure(ex, funding, "CoverArt", NullLogger.Instance);

        Assert.That(translated, Is.InstanceOf<AiCreditExhaustedException>());
    }

    /// <summary>Covers are portrait and key art landscape (feature 42); every shape has a size.</summary>
    [TestCase(Nornis.Application.Art.ImageShape.Portrait, 1024, 1536)]
    [TestCase(Nornis.Application.Art.ImageShape.Landscape, 1536, 1024)]
    public void EachShape_HasItsSize(Nornis.Application.Art.ImageShape shape, int width, int height) =>
        Assert.That(OpenAiImageArtGenerator.SizeOf(shape), Is.EqualTo((width, height)));

    [Test]
    public void EveryShape_HasASize() =>
        Assert.That(() => Enum.GetValues<Nornis.Application.Art.ImageShape>().Select(OpenAiImageArtGenerator.SizeOf).ToList(), Throws.Nothing);
}
