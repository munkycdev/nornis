using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>The check-in and the editorial brief (feature 44). GM-only throughout.</summary>
public interface ICheckInService
{
    /// <summary>Whether a check-in is due, the open one with its questions, and the standing instructions. Read-only.</summary>
    Task<AppResult<CheckInView>> GetAsync(Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Ask me now: gathers the evidence, makes the one AI call that chooses and phrases the
    /// questions, closes the previous check-in and opens the new one. Budget-guarded; a refused
    /// or failed call opens nothing.
    /// </summary>
    Task<AppResult<CheckInView>> StartAsync(Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>Answers one question: acts on the record, writes the standing instruction if given, stamps the question. Once only.</summary>
    Task<AppResult<CheckInAnswerResult>> AnswerAsync(AnswerCheckInQuestionCommand command, CancellationToken ct);

    /// <summary>Adds a standing instruction typed by the GM. Capped per world.</summary>
    Task<AppResult<EditorialRuleView>> AddRuleAsync(Guid worldId, string text, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>Removes a standing instruction. A rule already gone is gone.</summary>
    Task<AppResult> RemoveRuleAsync(Guid worldId, Guid ruleId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
