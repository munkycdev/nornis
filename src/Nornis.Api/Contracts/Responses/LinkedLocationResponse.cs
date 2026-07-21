namespace Nornis.Api.Contracts.Responses;

/// <summary>One Location artifact a session is linked to; Summary feeds the shared hover-card.</summary>
public record LinkedLocationResponse(Guid ArtifactId, string Name, string? Summary);
