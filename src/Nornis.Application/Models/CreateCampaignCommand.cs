namespace Nornis.Application.Models;

public record CreateCampaignCommand(
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatingUserId);
