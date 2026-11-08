using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// A world's key art (feature 42): one landscape picture in blob storage that stands for the
/// world, shown behind its name on Home, in the world switcher and on the public world page.
/// World-level, so every member sees it, and anyone with the public link when public access is on.
/// Null on the World means no key art, and the hero falls back to the map.
/// </summary>
public sealed record WorldKeyArt
{
    /// <summary>The longest brief: a paragraph, as for a cover.</summary>
    public const int MaxBriefChars = AdventureCover.MaxBriefChars;

    public required string BlobPath { get; init; }

    /// <summary>image/png or image/jpeg.</summary>
    public required string ContentType { get; init; }

    /// <summary>Uploaded or generated, as for a cover.</summary>
    public required CoverSource Source { get; init; }

    /// <summary>The GM's brief a generated picture was made from; null for an upload.</summary>
    public string? Brief { get; init; }

    public KeyArtAnchor Anchor { get; init; } = KeyArtAnchor.Centre;

    /// <summary>When this art was set: the Web's cache-buster for the image.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
