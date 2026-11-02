using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// An Adventure's side conversation (feature 30): the GM talks the adventure through with the
/// Loremaster, grounded in the whole record, and copies any answer into a section.
///
/// GM-only by the caller's current role, checked first on every call. As with drafting,
/// everything that can refuse a turn does so before the stream exists, and once it exists it
/// ends in exactly one <see cref="ConversationEvent.Completed"/> or
/// <see cref="ConversationEvent.Failed"/>, or the caller stops reading. Only
/// <see cref="ConversationEvent.Completed"/> saves an answer; the GM's message is saved before
/// the model is called, whatever happens to the answer.
/// </summary>
public interface IAdventureConversationService
{
    Task<AppResult<ConversationView>> GetAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct);

    Task<AppResult<IAsyncEnumerable<ConversationEvent>>> SendAsync(SendConversationMessageCommand command, CancellationToken ct);

    /// <summary>Deletes every message. The Adventure's sections and their revisions are untouched.</summary>
    Task<AppResult> ClearAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Put in section: one saved answer into a section, replacing or appending, through the
    /// revision path a draft uses, so Undo restores the section. No AI call.
    /// </summary>
    Task<AppResult<PutInSectionResult>> PutInSectionAsync(PutInSectionCommand command, CancellationToken ct);
}
