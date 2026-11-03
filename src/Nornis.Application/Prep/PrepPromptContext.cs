using System.Text;

namespace Nornis.Application.Prep;

/// <summary>
/// What every Prep prompt opens with (feature 37): the game system and levels (feature 35), the
/// table brief, the world's record and passages from the reference books. Built once per call by
/// <see cref="PrepPromptContextBuilder"/> and written by <see cref="AppendTo"/>, so drafting, the
/// side conversation, design mode and the split planner can't drift apart or forget the brief.
/// </summary>
/// <param name="WorldContext">The world's record with the reference passages, formatted by the
/// Loremaster's formatter so a book is cited the same way everywhere.</param>
/// <param name="ReferenceBooksUnavailable">The books couldn't be reached; the prompt says so rather
/// than letting the model think they have nothing to say.</param>
public sealed record PrepPromptContext(
    string? GameSystem,
    string? Levels,
    string? TableBrief,
    string WorldContext,
    bool ReferenceBooksUnavailable = false)
{
    /// <summary>The rules every Prep system prompt carries about the reference books.</summary>
    public const string ReferenceRules = """
        - Passages under "Published Reference" are from published books the GM owns. Use them for
          rules, monsters and setting detail, and cite them as (*Book title*, p. 12). Never copy a
          stat block or more than a sentence or two from them; point to the page instead.
        - The table brief says who is playing. Pitch difficulty, length and tone to it.
        """;

    public void AppendTo(StringBuilder sb)
    {
        sb.AppendLine("## The world");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(GameSystem) ? "Game system: not stated." : $"Game system: {GameSystem}");
        if (!string.IsNullOrWhiteSpace(Levels))
        {
            sb.AppendLine($"For characters of level {Levels.Trim()}.");
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(TableBrief))
        {
            sb.AppendLine("### The table");
            sb.AppendLine();
            sb.AppendLine(TableBrief.Trim());
            sb.AppendLine();
        }

        sb.AppendLine(string.IsNullOrWhiteSpace(WorldContext)
            ? "The world's record has nothing on this yet."
            : WorldContext.TrimEnd());
        sb.AppendLine();

        if (ReferenceBooksUnavailable)
        {
            sb.AppendLine("The reference books couldn't be reached for this request, so no passages from them are included.");
            sb.AppendLine();
        }
    }
}
