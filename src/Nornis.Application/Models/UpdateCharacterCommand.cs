using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateCharacterCommand(
    Guid CharacterId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? Name = null,
    string? Description = null,
    Guid? ArtifactId = null,
    bool UnlinkArtifact = false);
