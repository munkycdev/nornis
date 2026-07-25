namespace Nornis.Application.Models;

/// <summary>
/// Request to permanently delete a world. <paramref name="ConfirmationName"/> must match the
/// world's exact name — the server-side backstop for the type-to-confirm UI.
/// </summary>
public record DeleteWorldCommand(
    Guid WorldId,
    Guid ActingUserId,
    string? ConfirmationName);
