namespace Nornis.Api.Contracts.Responses;

public record InvitePreviewResponse(
    Guid WorldId,
    string WorldName,
    string Role,
    string Status);
