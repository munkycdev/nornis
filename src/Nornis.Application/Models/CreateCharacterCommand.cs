using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Creates a character owned by the acting member, or — GM only — by the member
/// identified by <paramref name="ForWorldMemberId"/>.
/// </summary>
public record CreateCharacterCommand(
    Guid WorldId,
    string Name,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? Description = null,
    Guid? ForWorldMemberId = null,
    Guid? ArtifactId = null);
