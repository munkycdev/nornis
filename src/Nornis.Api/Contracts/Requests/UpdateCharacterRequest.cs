namespace Nornis.Api.Contracts.Requests;

public record UpdateCharacterRequest(
    string? Name = null,
    string? Description = null);
