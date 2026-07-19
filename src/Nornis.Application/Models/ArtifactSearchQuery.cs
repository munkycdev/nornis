using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A global search across a world's artifacts. <paramref name="Limit"/> is clamped by the
/// service — this backs a type-ahead, so callers ask for a handful, not a page.
/// </summary>
public record ArtifactSearchQuery(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string Term,
    int Limit = 10);
