namespace Nornis.Api.Contracts.Responses;

/// <summary>The public face of a world — deliberately nothing beyond the card:
/// no budget figures, no membership, no ids beyond what public pages need.
/// <see cref="AskEnabled"/> is a capability flag (not the cap amount) so the public site
/// knows whether to offer "Ask the Loremaster".</summary>
public record PublicWorldResponse(
    string Slug,
    string Name,
    string? Description,
    string? GameSystem,
    bool AskEnabled);
