namespace Nornis.Api.Contracts.Requests;

public record AddWorldMemberRequest(
    Guid UserId,
    string Role);
