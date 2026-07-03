namespace Nornis.Application.Models;

public record UpdateCampaignCommand(
    Guid CampaignId,
    string? Name,
    string? Description,
    string? GameSystem,
    Guid ActingUserId);
