namespace Nornis.Api.Contracts.Requests;

public record UpdateWorldRequest(
    string? Name = null,
    string? Description = null,
    string? GameSystem = null);
