using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// An Adventure's cover art (feature 38): one image in blob storage and how the cover lays it
/// out. Null on the Adventure means no art, and the booklet keeps its typeset cover.
/// </summary>
public sealed record AdventureCover
{
    /// <summary>The longest art brief: a paragraph.</summary>
    public const int MaxBriefChars = 2_000;

    public required string BlobPath { get; init; }

    /// <summary>image/png or image/jpeg.</summary>
    public required string ContentType { get; init; }

    public required CoverSource Source { get; init; }

    /// <summary>The brief a generated cover was made from; null for an upload.</summary>
    public string? Brief { get; init; }

    public CoverLayout Layout { get; init; } = CoverLayout.FullBleed;

    /// <summary>When this art was set: the Web's cache-buster for the image.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
