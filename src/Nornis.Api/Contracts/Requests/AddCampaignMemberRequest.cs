namespace Nornis.Api.Contracts.Requests;

public record AddCampaignMemberRequest(
    Guid UserId,
    string Role);
