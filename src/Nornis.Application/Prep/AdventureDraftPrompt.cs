using System.Text;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>What one draft or revision of a section is written from.</summary>
/// <param name="WorldContext">The world knowledge block, already retrieved at the GM's role and
/// formatted. Hidden truth is in it because the asker is the GM; that rule lives in the
/// retriever, not here.</param>
/// <param name="Threads">Threads ready to pay off (feature 30), for an Overview draft only:
/// the convergence gauge's top candidates. Empty for every other section.</param>
public sealed record AdventureDraftInput(
    string? GameSystem,
    string WorldContext,
    Adventure Adventure,
    Guid SectionId,
    string? Instruction,
    IReadOnlyList<PrepThread>? Threads = null);

/// <summary>A hidden truth the convergence gauge ranks as ready to pay off: what it hangs on, and what it says.</summary>
public sealed record PrepThread(string AnchorName, string Description);

/// <summary>
/// The prompt for drafting or revising one Prep section (feature 29). The order is the cache
/// order: the system prompt never changes, then what changes least to what changes most, so
/// consecutive drafts in one Adventure share the longest prefix the provider can cache. The
/// world context, the outline and the Overview come before the neighbours, the section and
/// the instruction, which change with every call.
/// </summary>
public static class AdventureDraftPrompt
{
    /// <summary>Each neighbouring section is cut to this many characters; they are context, not the work.</summary>
    public const int MaxNeighbourChars = 6_000;

    /// <summary>How many threads to use an Overview draft is given, the gauge's strongest first.</summary>
    public const int MaxThreads = 5;

    /// <summary>Each thread's line is cut to this; a fact's value can be long, and these are pointers.</summary>
    public const int MaxThreadChars = 300;

    public static readonly string SystemPrompt = $$"""
        You are helping a game master prepare an adventure for their tabletop group. You write one
        section of their adventure at a time, in their voice, for them to run at the table. The GM
        decides what happens; you draft what they asked for and nothing beyond it.

        ## What you are given
        - The world's record: its people, places, factions, facts and relationships. Some are marked
          Hidden. Those are the GM's secrets, and you may use them, because only the GM reads this.
          Write them so the GM can run them, never as something the players are told outright,
          unless the GM asks for that.
        - The adventure's outline, its overview and pitch, and the sections either side of the one
          you are writing.
        - The section itself, and the GM's instruction for it.

        ## Rules
        - Stay consistent with the world's record. Do not contradict a Confirmed fact. Prefer
          existing names, places and factions to new ones; invent only what the section needs, and
          only where the record is silent.
        - An adventure is planned, not played. Write scenes as things that may happen, not as
          things that did.
        - Keep to the section you were asked for. Don't write the next scene, and don't summarise
          the adventure.
        - Do not include citation markers such as [ref:...]; this text is for the table.

        ## Format
        Answer with the section's body only, in Markdown, with no heading for the section's own
        title and no preamble. Use these conventions, which the GM's editor and the printed booklet
        both understand:
        {{PrepMarkup.ConventionsForPrompt}}

        Keep it to what one section needs: usually 200 to 900 words, never more than about 1,200.
        """;

    public static string BuildUserMessage(AdventureDraftInput input)
    {
        var sections = input.Adventure.Sections.OrderBy(s => s.SortOrder).ToList();
        var index = sections.FindIndex(s => s.Id == input.SectionId);
        if (index < 0)
        {
            throw new ArgumentException("The section isn't in this adventure.", nameof(input));
        }

        var target = sections[index];
        var sb = new StringBuilder();

        sb.AppendLine("## The world");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(input.GameSystem) ? "Game system: not stated." : $"Game system: {input.GameSystem}");
        if (!string.IsNullOrWhiteSpace(input.Adventure.Details.Levels))
        {
            sb.AppendLine($"For characters of level {input.Adventure.Details.Levels.Trim()}.");
        }

        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(input.WorldContext)
            ? "The world's record has nothing on this yet."
            : input.WorldContext.TrimEnd());
        sb.AppendLine();

        AppendThreads(sb, input.Threads);

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
            var marker = i == index ? "  <- the section you are writing" : "";
            sb.AppendLine($"{i + 1}. [{s.Kind}] {s.Title} ({state}){marker}");
        }

        sb.AppendLine();

        var overview = sections.FirstOrDefault(s => s.Kind == AdventureSectionKind.Overview && s.Id != target.Id);
        if (overview is not null && !AdventureSkeleton.IsUnfilled(overview.BodyMarkdown))
        {
            sb.AppendLine("### Overview");
            sb.AppendLine();
            sb.AppendLine(overview.BodyMarkdown.Trim());
            sb.AppendLine();
        }

        AppendNeighbour(sb, "The section before", index > 0 ? sections[index - 1] : null, overview);
        AppendNeighbour(sb, "The section after", index < sections.Count - 1 ? sections[index + 1] : null, overview);

        var revising = !AdventureSkeleton.IsUnfilled(target.BodyMarkdown);
        sb.AppendLine($"## The section to {(revising ? "revise" : "write")}: [{target.Kind}] {target.Title}");
        sb.AppendLine();
        if (revising || target.BodyMarkdown.Length > 0)
        {
            sb.AppendLine(revising ? "Its current text, to revise:" : "Its scaffold, to fill in:");
            sb.AppendLine();
            sb.AppendLine(target.BodyMarkdown.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## The GM's instruction");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(input.Instruction)
            ? (revising ? "Tighten and improve this section, keeping what it establishes." : "Write this section.")
            : input.Instruction.Trim());

        return sb.ToString();
    }

    private static void AppendThreads(StringBuilder sb, IReadOnlyList<PrepThread>? threads)
    {
        if (threads is not { Count: > 0 })
        {
            return;
        }

        sb.AppendLine("### Threads ready to pay off");
        sb.AppendLine();
        sb.AppendLine("Hidden truths the world's record ranks as closest to being revealed. Build the adventure "
            + "toward one or two of them if they fit the pitch; they are the GM's to reveal, not the players' to be told.");
        sb.AppendLine();
        foreach (var thread in threads.Take(MaxThreads))
        {
            var line = $"{thread.AnchorName}: {thread.Description}";
            sb.AppendLine($"- {(line.Length > MaxThreadChars ? line[..MaxThreadChars] + "…" : line)}");
        }

        sb.AppendLine();
    }

    private static void AppendNeighbour(StringBuilder sb, string label, AdventureSection? neighbour, AdventureSection? overview)
    {
        // The Overview is already above in full; an empty neighbour says nothing.
        if (neighbour is null || neighbour.Id == overview?.Id || AdventureSkeleton.IsUnfilled(neighbour.BodyMarkdown))
        {
            return;
        }

        var body = neighbour.BodyMarkdown.Trim();
        if (body.Length > MaxNeighbourChars)
        {
            body = body[..MaxNeighbourChars] + "\n[…cut]";
        }

        sb.AppendLine($"### {label}: [{neighbour.Kind}] {neighbour.Title}");
        sb.AppendLine();
        sb.AppendLine(body);
        sb.AppendLine();
    }
}
