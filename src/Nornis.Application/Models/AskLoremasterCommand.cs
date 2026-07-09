using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AskLoremasterCommand(
    Guid WorldId,
    string Question,
    Guid UserId,
    WorldRole UserRole,
    string? ConversationContext);
