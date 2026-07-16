namespace Nornis.Api.Contracts.Responses;

/// <summary>The public face of a world — deliberately nothing beyond the card:
/// no budget, no membership, no ids beyond what public pages need.</summary>
public record PublicWorldResponse(
    string Slug,
    string Name,
    string? Description,
    string? GameSystem);
