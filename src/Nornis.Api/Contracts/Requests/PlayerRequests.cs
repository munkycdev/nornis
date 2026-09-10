namespace Nornis.Api.Contracts.Requests;

public record CreatePlayerRequest(string Name);

public record RenamePlayerRequest(string Name);

public record LinkPlayerRequest(Guid WorldMemberId);

public record MoveCharacterRequest(Guid PlayerId);
