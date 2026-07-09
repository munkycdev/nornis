namespace Nornis.Api.Contracts.Requests;

public record CreateCharacterRequest(
    string Name,
    string? Description = null,
    Guid? WorldMemberId = null);
