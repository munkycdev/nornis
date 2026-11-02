using System.Text;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>What one turn of an Adventure's side conversation is answered from.</summary>
/// <param name="WorldContext">The world knowledge block, retrieved at the GM's role and
/// formatted. Hidden truth is in it because the asker is the GM; that rule lives in the
/// retriever, not here.</param>
/// <param name="SectionId">The section on screen when the GM sent the message. Null when none was.</param>
/// <param name="History">The conversation so far that fits the window, oldest first, without
/// the new message. <see cref="AdventureConversationPrompt.HistoryWindow"/> chooses it.</param>
/// <param name="MaxSectionChars">The section on screen is cut to this.</param>
public sealed record AdventureConversationInput(
    string? GameSystem,
    string WorldContext,
    Adventure Adventure,
    Guid? SectionId,
    IReadOnlyList<ConversationMessage> History,
    string Message,
    int MaxSectionChars);

/// <summary>
/// The prompt for one turn of an Adventure's side conversation (feature 30). Cache-ordered like
/// <see cref="AdventureDraftPrompt"/>: the fixed system prompt, then the world, the Adventure,
/// the section on screen, the conversation so far and the new message, from what changes least
/// to what changes most.
///
/// The conversation rides as a transcript inside the one user message rather than as separate
/// chat turns, so every Prep call is the same shape: one system prompt, one user message, and
/// the drafting client's output ceiling.
/// </summary>
public static class AdventureConversationPrompt
{
    public const string GmLabel = "GM";
    public const string LoremasterLabel = "Loremaster";

    public static readonly string SystemPrompt = $$"""
        You are the Loremaster, talking an adventure through with the game master who is preparing
        it for their tabletop group. The GM is the author: you are a planning partner who knows their
        world. You help them think, and you write for the adventure only when they ask you to.

        ## What you are given
        - The world's record: its people, places, factions, facts and relationships. Some are marked
          Hidden. Those are the GM's secrets, and you may use them, because only the GM reads this
          conversation. Treat them as things the GM can reveal, never as things the players know.
        - The adventure's title, pitch and outline, and the section the GM has open, if any.
        - The conversation so far, and the GM's new message.

        ## Rules
        - Stay consistent with the world's record. Do not contradict a Confirmed fact. Prefer
          existing names, places and factions to new ones, and say so when you invent something.
        - An adventure is planned, not played. Talk about what may happen, not what did.
        - Be brief and concrete. When the GM asks for ideas, offer a few distinct options, each in a
          sentence or two, rather than one long answer.
        - Do not include citation markers such as [ref:...].

        ## When the GM asks you to write something for the adventure
        Write it so it can go into a section as it stands: Markdown, no heading for the section's
        own title, no preamble or sign-off around it. Use these conventions, which the GM's editor
        and the printed booklet both understand:
        - Read-aloud text is a plain blockquote: lines starting with "> ".
        - A prop the players can hold or read is a blockquote whose first line is exactly
          "> {{PrepMarkup.PropMarker}}", followed by its text on the next "> " lines.
        - A question the GM puts to the players is a blockquote whose first line is exactly
          "> {{PrepMarkup.AskMarker}}", followed by the question.
        - A stat block is a fenced code block with the info string "{{PrepMarkup.StatBlockInfo}}",
          one "Label: value" pair per line.
        - Tables are Markdown pipe tables. Subheadings start at "###".
        """;

    /// <summary>
    /// The conversation so far that rides with a turn: the newest whole messages whose lengths
    /// add up to at most <paramref name="maxChars"/>, returned oldest first. A message is never
    /// cut; one that would cross the window ends it, so the window never skips a message to fit
    /// an older one.
    /// </summary>
    public static IReadOnlyList<ConversationMessage> HistoryWindow(IReadOnlyList<ConversationMessage> earlier, int maxChars)
    {
        var window = new List<ConversationMessage>();
        var used = 0;
        for (var i = earlier.Count - 1; i >= 0; i--)
        {
            var length = earlier[i].Content.Length;
            if (used + length > maxChars)
            {
                break;
            }

            used += length;
            window.Add(earlier[i]);
        }

        window.Reverse();
        return window;
    }

    public static string BuildUserMessage(AdventureConversationInput input)
    {
        var sections = input.Adventure.Sections.OrderBy(s => s.SortOrder).ToList();
        var onScreen = input.SectionId is { } id ? sections.FirstOrDefault(s => s.Id == id) : null;
        var sb = new StringBuilder();

        sb.AppendLine("## The world");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(input.GameSystem) ? "Game system: not stated." : $"Game system: {input.GameSystem}");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(input.WorldContext)
            ? "The world's record has nothing on this yet."
            : input.WorldContext.TrimEnd());
        sb.AppendLine();

        sb.AppendLine($"## The adventure: {input.Adventure.Title}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(input.Adventure.Pitch))
        {
            sb.AppendLine($"Pitch: {input.Adventure.Pitch.Trim()}");
            sb.AppendLine();
        }

        sb.AppendLine("### Outline");
        sb.AppendLine();
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var state = AdventureSkeleton.IsUnfilled(s.BodyMarkdown) ? "not written yet" : "written";
            var marker = s.Id == onScreen?.Id ? "  <- open on the GM's screen" : "";
            sb.AppendLine($"{i + 1}. [{s.Kind}] {s.Title} ({state}){marker}");
        }

        sb.AppendLine();

        if (onScreen is not null)
        {
            sb.AppendLine($"## The section the GM has open: [{onScreen.Kind}] {onScreen.Title}");
            sb.AppendLine();
            if (AdventureSkeleton.IsUnfilled(onScreen.BodyMarkdown))
            {
                sb.AppendLine("Not written yet.");
            }
            else
            {
                var body = onScreen.BodyMarkdown.Trim();
                sb.AppendLine(body.Length > input.MaxSectionChars ? body[..input.MaxSectionChars] + "\n[…cut]" : body);
            }

            sb.AppendLine();
        }

        sb.AppendLine("## The conversation so far");
        sb.AppendLine();
        if (input.History.Count == 0)
        {
            sb.AppendLine("This is the first message.");
            sb.AppendLine();
        }
        else
        {
            foreach (var message in input.History)
            {
                sb.AppendLine($"{Label(message.Role)}: {message.Content.Trim()}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## The GM's new message");
        sb.AppendLine();
        sb.AppendLine(input.Message.Trim());

        return sb.ToString();
    }

    private static string Label(ConversationRole role) => role == ConversationRole.User ? GmLabel : LoremasterLabel;
}
