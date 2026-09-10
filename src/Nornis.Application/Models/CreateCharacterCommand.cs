using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Creates a character for the acting member's own player, or — GM only — for the player
/// identified by <paramref name="ForPlayerId"/>, who need not be on Nornis.
/// </summary>
public record CreateCharacterCommand(
    Guid WorldId,
    string Name,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? Description = null,
    Guid? ForPlayerId = null,
    Guid? ArtifactId = null);
