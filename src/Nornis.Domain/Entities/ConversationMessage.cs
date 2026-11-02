using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One message of a <see cref="Conversation"/>. A GM's message is saved before the model is
/// called, so a failed answer never loses the question; an answer is saved only when it ended
/// cleanly.
/// </summary>
public class ConversationMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public ConversationRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>The model that wrote an answer. Null on the GM's own messages.</summary>
    public string? Model { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
