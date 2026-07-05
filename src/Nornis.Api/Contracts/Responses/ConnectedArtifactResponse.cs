namespace Nornis.Api.Contracts.Responses;

public record ConnectedArtifactResponse(
    Guid Id,
    string Name,
    string Type);
