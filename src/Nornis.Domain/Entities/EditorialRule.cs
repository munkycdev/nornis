using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One standing instruction the GM has given Nornis about what to track in this world, or how
/// to record it (feature 44). The world's rules together are its editorial brief, which the
/// extraction prompt reads on every source. Capped per world and per line, because the brief
/// rides on every extraction call and must not become a second prompt.
/// </summary>
public class EditorialRule
{
    public const int MaxTextChars = 300;

    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public string Text { get; set; } = string.Empty;

    public EditorialRuleOrigin Origin { get; set; }

    /// <summary>The check-in question whose answer wrote this rule; null for a rule typed by hand.</summary>
    public Guid? CheckInQuestionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;
}
