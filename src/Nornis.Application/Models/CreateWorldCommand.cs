namespace Nornis.Application.Models;

public record CreateWorldCommand(
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatingUserId);
