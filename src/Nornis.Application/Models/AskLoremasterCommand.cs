using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A request to Ask the Loremaster. <paramref name="UserId"/> is null for anonymous public
/// asks — retrieval falls back to the empty-guid Observer sentinel, and the cost-ledger row is
/// written with no user (which is also how public spend is metered). <paramref name="IncludeLibrary"/>
/// is false for public asks so anonymous readers never receive indexed sourcebook passages.
/// </summary>
public record AskLoremasterCommand(
    Guid WorldId,
    string Question,
    Guid? UserId,
    WorldRole UserRole,
    string? ConversationContext,
    bool IncludeLibrary = true);
