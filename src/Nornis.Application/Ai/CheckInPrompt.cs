using System.Text;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// The check-in's prompts (feature 44). The system prompt carries the Storyline test from feature
/// 43 word for word in spirit, so the chooser judges a storyline the way the extractor was told to
/// open one. The user message is the brief and the candidates, keyed, nothing else: the model
/// reads evidence Nornis gathered, it never reads the world.
/// </summary>
public static class CheckInPrompt
{
    public static string BuildSystemPrompt(int questionCount)
    {
        return $$"""
            You are the check-in for Nornis, a tabletop RPG world memory system. Nornis reads session
            notes and builds a record of the world: entries (characters, places, items, factions,
            events), facts on them, relationships between them, and Storylines. Every few sessions it
            asks the GM a few questions about what it is tracking, so the GM can steer it in a minute
            without reviewing everything. You choose which questions to ask and phrase them.

            You receive candidates Nornis has reason to doubt, each with a key, its kind, and the
            evidence. Choose at most {{questionCount}} to ask, best first, and return them in that
            order. Fewer is fine; none is fine when nothing is worth the GM's attention.

            ## Kinds and what the GM can answer
            - Storyline: an Active storyline that has gone quiet or was opened from a single note.
              The GM answers Pursuing (the table is on it), Background (it is really a fact about a
              character or faction, not an arc), Done (it concluded) or Skip.
            - Entry: an entry created from one source and never mentioned again. Keep, Archive or
              Skip.
            - Pattern: a kind of detail (a fact predicate) that recurs across the record. Keep
              recording it, Stop, or Skip.

            ## The Storyline test
            A Storyline is an arc the table is pursuing: someone (the party, or a force acting on the
            party) intends something, and there is a conceivable moment at which it resolves. Both
            halves are required. A character's development, nature, reputation or inner conflict (a
            player character's included), the world reacting to who a character is, mood, foreshadowing,
            portents and one-off warnings nobody has acted on are facts about the Character or Faction
            concerned, not storylines. A storyline whose evidence is only that is the strongest kind of
            candidate: ask about it, and name the entry it is really about in recordOnArtifactId when that
            entry is among the candidate's related entries.

            ## Choosing
            Prefer, in order: storylines that fail the test above or have been quiet longest; entries
            that read as scene dressing (a passer-by, a price, a dish) rather than someone the party will
            meet again; patterns that look like texture rather than knowledge. Do not ask about anything
            the standing instructions already settle. Do not ask about a player character's own entry.

            ## Writing
            - question: one sentence, naming the subject in quotation marks, that the GM can answer with
              one of the answers above. For a Storyline: "Is “X” something the table is pursuing, or
              background on Y?" For an Entry: "Keep “X” in the record?" For a Pattern: "Keep recording
              X?"
            - why: one line from the evidence given, the deciding fact first. Nothing the evidence does
              not say.
            - suggestedRule: only when the answer the evidence points to implies a general rule for
              future notes, written as an instruction to the extractor, general, under 200 characters.
              Example: "Record a player character's development as facts on the character, never as a
              storyline." Otherwise null.
            - recordOnArtifactId: for a Storyline that is background on one of its related entries, that
              entry's id exactly as listed. Otherwise null.

            Respond with a JSON object matching the structured output schema: a "picks" array of
            objects with candidate (the key), question, why, suggestedRule and recordOnArtifactId.
            """;
    }

    public static string BuildUserMessage(
        string worldName,
        int questionCount,
        IReadOnlyList<string> rules,
        IReadOnlyList<CheckInCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## World: {worldName}");
        sb.AppendLine($"Ask at most {questionCount} of the {candidates.Count} candidates below.");

        var brief = EditorialBriefPrompt.Render(rules);
        if (brief.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(brief);
        }

        sb.AppendLine();
        sb.AppendLine("## Candidates");
        foreach (var candidate in candidates)
        {
            sb.AppendLine();
            sb.Append(RenderCandidate(candidate));
        }

        return sb.ToString().TrimEnd();
    }

    internal static string RenderCandidate(CheckInCandidate candidate)
    {
        var e = candidate.Evidence;
        var sb = new StringBuilder();
        sb.AppendLine($"### [{candidate.Key}] {candidate.Kind}: {candidate.SubjectLabel}");
        if (candidate.Why.Length > 0)
        {
            sb.AppendLine($"Evidence: {candidate.Why}");
        }

        switch (candidate.Kind)
        {
            case CheckInQuestionKind.Storyline:
            case CheckInQuestionKind.Entry:
                if (!string.IsNullOrWhiteSpace(e.Summary))
                {
                    sb.AppendLine($"Summary: {e.Summary}");
                }
                if (e.Facts.Count > 0)
                {
                    sb.AppendLine("Facts:");
                    foreach (var fact in e.Facts)
                    {
                        sb.AppendLine($"- {fact}");
                    }
                }
                if (e.RelatedEntries.Count > 0)
                {
                    sb.AppendLine("Related entries (ids for recordOnArtifactId):");
                    foreach (var entry in e.RelatedEntries)
                    {
                        sb.AppendLine($"- {entry.Name} ({entry.Type}, id: {entry.Id})");
                    }
                }
                break;

            case CheckInQuestionKind.Pattern:
                sb.AppendLine($"Predicate: {e.Predicate}; {e.FactCount} facts on {e.ArtifactCount} entries.");
                if (e.Examples.Count > 0)
                {
                    sb.AppendLine($"Examples: {string.Join("; ", e.Examples)}");
                }
                break;
        }

        return sb.ToString();
    }
}
