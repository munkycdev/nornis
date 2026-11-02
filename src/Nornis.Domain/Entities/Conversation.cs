namespace Nornis.Domain.Entities;

/// <summary>
/// Saved chat between a GM and the Loremaster (feature 30). In 3.1 every Conversation is an
/// Adventure's side conversation: at most one per Adventure, GM-only by the caller's current
/// role, never extracted, never read by Ask, never public. Its answers reach the Adventure only
/// when the GM chooses Put in section, and the record only through Export and review.
///
/// No <c>Visibility</c> column, for the Adventure's reason: it is written with Hidden truth in
/// context, so a column that could say PartyVisible would be a guard that must never fire.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>The GM who started it.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The Adventure this is the side conversation of. Null would be a conversation that isn't
    /// Prep, which the domain model allows and 3.1 never creates.
    /// </summary>
    public Guid? AdventureId { get; set; }

    /// <summary>The Adventure's title when the conversation started.</summary>
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public World World { get; set; } = null!;

    public Adventure? Adventure { get; set; }

    public User User { get; set; } = null!;

    public ICollection<ConversationMessage> Messages { get; set; } = [];
}
