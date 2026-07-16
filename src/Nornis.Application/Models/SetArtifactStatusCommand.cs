using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record SetArtifactStatusCommand(
    Guid ArtifactId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    ArtifactStatus Status);
