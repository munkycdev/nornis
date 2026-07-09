namespace Nornis.Api.Contracts.Requests;

public record CreateWorldRequest(
    string Name,
    string? Description = null,
    string? GameSystem = null);
