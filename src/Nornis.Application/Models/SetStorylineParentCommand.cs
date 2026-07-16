using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Sets (or clears, when <paramref name="ParentArtifactId"/> is null) the storyline a
/// storyline belongs to, expressed as a reserved "PartOf" relationship.
/// </summary>
public record SetStorylineParentCommand(
    Guid ArtifactId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    Guid? ParentArtifactId);
