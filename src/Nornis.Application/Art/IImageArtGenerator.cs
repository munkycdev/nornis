using Nornis.Application.Ai;

namespace Nornis.Application.Art;

/// <summary>
/// The shape of a generated picture. A cover is a page (portrait); a world's key art is a band
/// across the top of Home (landscape). The generator maps each to the nearest size the image
/// models make.
/// </summary>
public enum ImageShape
{
    Portrait,
    Landscape,
}

/// <summary>One generated image: PNG bytes, and the prompt as the model rewrote it, when it says.</summary>
public sealed record GeneratedImage(byte[] Png, string? RevisedPrompt);

/// <summary>A generation's images and what it cost.</summary>
public sealed record ImageArtGeneration(IReadOnlyList<GeneratedImage> Images, AiUsage Usage);

/// <summary>
/// The image model rejected the request itself (HTTP 400). <see cref="Moderation"/> is its safety
/// system turning the prompt down, which rewording can fix; anything else is the provider saying
/// why in <see cref="Reason"/> (its own words, never a key), which rewording won't.
/// </summary>
public sealed class ImageArtRejectedException : AiHttpException
{
    public ImageArtRejectedException(bool moderation, string? reason, Exception? innerException = null)
        : base($"The image model rejected the request{(moderation ? " (moderation)" : string.Empty)}: {reason}", 400, innerException)
    {
        Moderation = moderation;
        Reason = reason;
    }

    public bool Moderation { get; }

    public string? Reason { get; }
}

/// <summary>
/// Makes pictures from a prompt, on the key holder's image model through the keys feature's
/// funding rule. Only a user's own key pays: with none it refuses. One generator for covers
/// (feature 38) and key art (feature 42), so the key binding, the error mapping and the pricing
/// live in one place; the callers differ only in the shape they ask for and the operation they
/// name in the logs.
/// </summary>
public interface IImageArtGenerator
{
    /// <exception cref="AiRefusalException">No key can make images, or the paying key was refused.</exception>
    /// <exception cref="ImageArtRejectedException">The provider rejected the request: moderation, or a reason it names.</exception>
    /// <exception cref="AiHttpException">The provider failed otherwise.</exception>
    Task<ImageArtGeneration> GenerateAsync(
        AiCallScope scope, string prompt, ImageShape shape, int count, int timeoutSeconds, string operation, CancellationToken ct);

    /// <summary>Whether a generation in <paramref name="scope"/> would find a key to pay: the Generate buttons.</summary>
    Task<bool> CanGenerateAsync(AiCallScope scope, CancellationToken ct);
}
