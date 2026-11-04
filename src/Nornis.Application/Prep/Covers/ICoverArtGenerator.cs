using Nornis.Application.Ai;

namespace Nornis.Application.Prep.Covers;

/// <summary>One generated cover image: PNG bytes, and the prompt as the model rewrote it, when it says.</summary>
public sealed record GeneratedCoverImage(byte[] Png, string? RevisedPrompt);

/// <summary>A generation's images and what it cost.</summary>
public sealed record CoverArtGeneration(IReadOnlyList<GeneratedCoverImage> Images, AiUsage Usage);

/// <summary>
/// The image model rejected the request itself (HTTP 400). <see cref="Moderation"/> is its safety
/// system turning the prompt down, which rewording can fix; anything else is the provider saying
/// why in <see cref="Reason"/> (its own words, never a key), which rewording won't.
/// </summary>
public sealed class CoverArtRejectedException : AiHttpException
{
    public CoverArtRejectedException(bool moderation, string? reason, Exception? innerException = null)
        : base($"The image model rejected the request{(moderation ? " (moderation)" : string.Empty)}: {reason}", 400, innerException)
    {
        Moderation = moderation;
        Reason = reason;
    }

    public bool Moderation { get; }

    public string? Reason { get; }
}

/// <summary>
/// Makes cover images from a prompt (feature 38), on the key holder's image model through the
/// keys feature's funding rule. Only a user's own key pays: with none it refuses.
/// </summary>
public interface ICoverArtGenerator
{
    /// <exception cref="AiRefusalException">No key can make images, or the paying key was refused.</exception>
    /// <exception cref="CoverArtRejectedException">The provider rejected the request: moderation, or a reason it names.</exception>
    /// <exception cref="AiHttpException">The provider failed otherwise.</exception>
    Task<CoverArtGeneration> GenerateAsync(AiCallScope scope, string prompt, int count, int timeoutSeconds, CancellationToken ct);

    /// <summary>Whether a generation in <paramref name="scope"/> would find a key to pay: the Cover card's Generate button.</summary>
    Task<bool> CanGenerateAsync(AiCallScope scope, CancellationToken ct);
}
