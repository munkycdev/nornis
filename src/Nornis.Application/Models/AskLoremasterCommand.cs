using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AskLoremasterCommand(
    Guid CampaignId,
    string Question,
    Guid UserId,
    CampaignRole UserRole,
    string? ConversationContext);
