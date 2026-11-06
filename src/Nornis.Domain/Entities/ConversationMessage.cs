using Nornis.Domain.Enums;
using Nornis.Domain.Models;

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

    /// <summary>
    /// What a research answer found on the web (feature 40), shown under it and never copied into
    /// the adventure. Null: the turn wasn't a research turn, which every message before 3.11 and
    /// every GM message is.
    /// </summary>
    public WebResearchResult? WebResearch { get; set; }

    /// <summary>
    /// What became of each of a Design turn's edits, in the answer's order (feature 41): the
    /// server applies them to the draft as the answer ends. Null on everything else: a GM's
    /// message, a Section turn's answer, an answer with no edits, and every answer from before
    /// 3.12, whose edits are applied only by hand. Never empty.
    /// </summary>
    public IReadOnlyList<DraftEditOutcome>? DraftEditOutcomes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
