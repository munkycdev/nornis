using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>A GM's request to remove one artifact from canon, tearing down the knowledge
/// hanging off it (its facts, the relationships touching it, its map pins, and provenance).</summary>
public record RemoveArtifactCommand(
    Guid WorldId,
    Guid ArtifactId,
    Guid ActingUserId,
    WorldRole ActingUserRole);
