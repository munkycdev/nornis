using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record RenameArtifactCommand(
    Guid ArtifactId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string Name);
