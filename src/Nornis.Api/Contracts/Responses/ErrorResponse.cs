namespace Nornis.Api.Contracts.Responses;

public record ErrorResponse(
    string Code,
    string Message);
