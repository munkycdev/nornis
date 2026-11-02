using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// An Adventure's side conversation (feature 30). Read only by Prep: no retriever, extractor or
/// public route uses this repository, which is how "never canon" holds.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// The Adventure's conversation with its messages oldest first, untracked. Null until the GM
    /// sends the first message, or after a Clear. The messages are bounded by
    /// <c>PrepOptions.MaxConversationMessages</c>, so reading them all is bounded too.
    /// </summary>
    Task<Conversation?> GetByAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the conversation, with any messages on its navigation, in one save.</summary>
    Task CreateAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>Appends one message and bumps the conversation's <c>UpdatedAt</c> to its time.</summary>
    Task AddMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default);

    /// <summary>One message with its conversation, untracked. Null when it doesn't exist.</summary>
    Task<ConversationMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the Adventure's conversation and every message in it. Nothing else.</summary>
    Task DeleteByAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default);
}
