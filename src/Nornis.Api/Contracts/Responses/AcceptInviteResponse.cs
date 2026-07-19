namespace Nornis.Api.Contracts.Responses;

public record AcceptInviteResponse(
    Guid WorldId,
    string WorldName,
    bool AlreadyMember);
