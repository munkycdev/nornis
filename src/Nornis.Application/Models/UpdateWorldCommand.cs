namespace Nornis.Application.Models;

public record UpdateWorldCommand(
    Guid WorldId,
    string? Name,
    string? Description,
    string? GameSystem,
    Guid ActingUserId);
