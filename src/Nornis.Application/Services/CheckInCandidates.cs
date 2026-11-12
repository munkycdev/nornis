using System.Text.RegularExpressions;
using Nornis.Application.Common;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Turns the record into the bounded list of things Nornis has reason to doubt (feature 44). A
/// pure function over <see cref="CheckInEvidence"/>, so every rule is tested on a hand-built
/// world and the service only gathers. Order is rank order: it decides what the chooser reads
/// first and how the unpicked questions are listed on the page.
/// </summary>
public static class CheckInCandidates
{
    /// <summary>Predicates that structure a storyline rather than describe the world; never a pattern.</summary>
    public static readonly IReadOnlySet<string> StructuralPredicates =
        new HashSet<string>(StringComparer.Ordinal) { OpenQuestionFact.Predicate, "goal", "development" };

    private const int MaxFactLines = 8;
    private const int MaxExampleChars = 80;
    private const int MaxExamples = 3;

    public static IReadOnlyList<CheckInCandidate> Build(CheckInEvidence evidence, CheckInOptions options)
    {
        var artifactsById = evidence.Artifacts.ToDictionary(a => a.Id);
        var factsByArtifact = evidence.Facts.GroupBy(f => f.ArtifactId).ToDictionary(g => g.Key, g => g.ToList());
        var relationshipsByArtifact = evidence.Relationships
            .SelectMany(r => new[] { (ArtifactId: r.ArtifactAId, Relationship: r), (ArtifactId: r.ArtifactBId, Relationship: r) })
            .GroupBy(x => x.ArtifactId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Relationship).ToList());
        var sourcesByTarget = evidence.References
            .GroupBy(r => r.TargetId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.SourceId).ToHashSet());
        var sourcesById = evidence.Sources.ToDictionary(s => s.Id);
        var recentSessionIds = evidence.Sessions.Take(options.CadenceSessions).Select(s => s.Id).ToHashSet();

        HashSet<Guid> SourcesBehind(Artifact artifact)
        {
            var sources = new HashSet<Guid>();
            if (sourcesByTarget.TryGetValue(artifact.Id, out var own))
            {
                sources.UnionWith(own);
            }
            foreach (var fact in factsByArtifact.GetValueOrDefault(artifact.Id) ?? [])
            {
                if (sourcesByTarget.TryGetValue(fact.Id, out var viaFact))
                {
                    sources.UnionWith(viaFact);
                }
            }
            foreach (var relationship in relationshipsByArtifact.GetValueOrDefault(artifact.Id) ?? [])
            {
                if (sourcesByTarget.TryGetValue(relationship.Id, out var viaRelationship))
                {
                    sources.UnionWith(viaRelationship);
                }
            }
            return sources;
        }

        Source? FirstSeen(IEnumerable<Guid> sourceIds) => sourceIds
            .Select(id => sourcesById.GetValueOrDefault(id))
            .Where(s => s is not null)
            .OrderBy(s => s!.OccurredAt ?? s.CreatedAt)
            .FirstOrDefault();

        var candidates = new List<CheckInCandidate>();
        candidates.AddRange(Storylines(evidence, options, artifactsById, factsByArtifact, relationshipsByArtifact, SourcesBehind, FirstSeen));
        candidates.AddRange(Entries(evidence, options, factsByArtifact, recentSessionIds, SourcesBehind, FirstSeen));
        candidates.AddRange(Patterns(evidence, options));

        // Keys are positional so the chooser's answer maps back without ids in the prompt.
        return candidates.Select((c, i) => c with { Key = $"c{i + 1}" }).ToList();
    }

    private static IEnumerable<CheckInCandidate> Storylines(
        CheckInEvidence evidence,
        CheckInOptions options,
        IReadOnlyDictionary<Guid, Artifact> artifactsById,
        IReadOnlyDictionary<Guid, List<ArtifactFact>> factsByArtifact,
        IReadOnlyDictionary<Guid, List<ArtifactRelationship>> relationshipsByArtifact,
        Func<Artifact, HashSet<Guid>> sourcesBehind,
        Func<IEnumerable<Guid>, Source?> firstSeen)
    {
        var scored = new List<(CheckInCandidate Candidate, int Score, DateTimeOffset CreatedAt)>();

        foreach (var storyline in evidence.Artifacts.Where(a =>
                     a.Type == ArtifactType.Storyline && a.Status == ArtifactStatus.Active
                     && !evidence.RecentlyAnsweredArtifactIds.Contains(a.Id)))
        {
            var sources = sourcesBehind(storyline);
            var quiet = evidence.QuietStorylines.GetValueOrDefault(storyline.Id);
            // Exactly one, as for entries: a storyline with no source behind it was the GM's own
            // doing, and the quiet signal still finds it once it has developments.
            var oneSource = sources.Count == 1;
            if (quiet == 0 && !oneSource)
            {
                continue;
            }

            var facts = factsByArtifact.GetValueOrDefault(storyline.Id) ?? [];
            var openQuestions = facts.Count(OpenQuestionFact.IsOpenQuestion);
            var first = firstSeen(sources);
            var related = RelatedEntries(storyline, facts, relationshipsByArtifact.GetValueOrDefault(storyline.Id) ?? [], artifactsById);

            var why = new List<string>();
            if (oneSource)
            {
                why.Add(first is null ? "Opened from one note" : $"Opened from “{first.Title}”, nothing since");
            }
            if (quiet > 0)
            {
                why.Add($"quiet for {quiet} session{(quiet == 1 ? "" : "s")}");
            }
            if (openQuestions > 0)
            {
                why.Add($"{openQuestions} open question{(openQuestions == 1 ? "" : "s")}");
            }

            var evidenceBlock = new CheckInEvidenceBlock
            {
                Summary = storyline.Summary,
                Status = storyline.Status.ToString(),
                FactCount = facts.Count,
                SourceCount = sources.Count,
                SessionsQuiet = quiet,
                OpenQuestionCount = openQuestions,
                FirstSeenTitle = first?.Title,
                FirstSeenAt = first?.OccurredAt ?? first?.CreatedAt,
                Facts = FactLines(facts),
                RelatedEntries = related,
            };

            scored.Add((new CheckInCandidate(
                Key: string.Empty,
                Kind: CheckInQuestionKind.Storyline,
                SubjectArtifactId: storyline.Id,
                SubjectLabel: storyline.Name,
                Text: $"Is “{storyline.Name}” something the table is pursuing, or background?",
                Why: Sentence(why),
                Evidence: evidenceBlock), quiet + (oneSource ? 3 : 0) + openQuestions, storyline.CreatedAt));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Candidate.SubjectLabel, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxStorylineCandidates)
            .Select(x => x.Candidate);
    }

    private static IEnumerable<CheckInCandidate> Entries(
        CheckInEvidence evidence,
        CheckInOptions options,
        IReadOnlyDictionary<Guid, List<ArtifactFact>> factsByArtifact,
        IReadOnlySet<Guid> recentSessionIds,
        Func<Artifact, HashSet<Guid>> sourcesBehind,
        Func<IEnumerable<Guid>, Source?> firstSeen)
    {
        var found = new List<(CheckInCandidate Candidate, int FactCount, DateTimeOffset CreatedAt)>();

        foreach (var entry in evidence.Artifacts.Where(a =>
                     a.Type != ArtifactType.Storyline && a.Status != ArtifactStatus.Archived
                     && !evidence.RecentlyAnsweredArtifactIds.Contains(a.Id)))
        {
            var sources = sourcesBehind(entry);
            // Exactly one: an entry with none was made by hand and is the GM's own doing.
            if (sources.Count != 1 || recentSessionIds.Contains(sources.First()))
            {
                continue;
            }

            var facts = factsByArtifact.GetValueOrDefault(entry.Id) ?? [];
            var first = firstSeen(sources);
            var why = first is null
                ? $"Mentioned once; {Count(facts.Count, "fact")}"
                : $"Mentioned once, in “{first.Title}”; {Count(facts.Count, "fact")}";

            found.Add((new CheckInCandidate(
                Key: string.Empty,
                Kind: CheckInQuestionKind.Entry,
                SubjectArtifactId: entry.Id,
                SubjectLabel: entry.Name,
                Text: $"Keep “{entry.Name}” ({DisplayType(entry.Type)}) in the record?",
                Why: why + ".",
                Evidence: new CheckInEvidenceBlock
                {
                    Summary = entry.Summary,
                    Status = entry.Status.ToString(),
                    FactCount = facts.Count,
                    SourceCount = 1,
                    FirstSeenTitle = first?.Title,
                    FirstSeenAt = first?.OccurredAt ?? first?.CreatedAt,
                    Facts = FactLines(facts),
                }), facts.Count, entry.CreatedAt));
        }

        return found
            .OrderBy(x => x.FactCount)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Candidate.SubjectLabel, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxEntryCandidates)
            .Select(x => x.Candidate);
    }

    private static IEnumerable<CheckInCandidate> Patterns(CheckInEvidence evidence, CheckInOptions options)
    {
        var liveArtifactIds = evidence.Artifacts.Where(a => a.Status != ArtifactStatus.Archived).Select(a => a.Id).ToHashSet();

        return evidence.Facts
            .Where(f => liveArtifactIds.Contains(f.ArtifactId))
            .GroupBy(f => PredicateKey(f.Predicate))
            .Where(g => !StructuralPredicates.Contains(g.Key) && !evidence.SettledPredicates.Contains(g.Key))
            .Select(g => (Key: g.Key, Facts: g.ToList(), Artifacts: g.Select(f => f.ArtifactId).Distinct().Count()))
            .Where(p => p.Facts.Count >= options.MinPatternFacts && p.Artifacts >= options.MinPatternArtifacts)
            .OrderByDescending(p => p.Facts.Count)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(options.MaxPatternCandidates)
            .Select(p =>
            {
                // The predicate as most often written, so the question reads the way the record does.
                var label = p.Facts.GroupBy(f => f.Predicate.Trim(), StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .First().Key;
                var examples = p.Facts.Select(f => f.Value.Trim())
                    .Where(v => v.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxExamples)
                    .Select(v => v.Truncate(MaxExampleChars, ellipsis: true))
                    .ToList();
                return new CheckInCandidate(
                    Key: string.Empty,
                    Kind: CheckInQuestionKind.Pattern,
                    SubjectArtifactId: null,
                    SubjectLabel: label,
                    Text: $"Keep recording “{label}”?",
                    Why: $"{Count(p.Facts.Count, "fact")} across {Count(p.Artifacts, "entry", "entries")}"
                         + (examples.Count > 0 ? $", e.g. {string.Join("; ", examples)}" : string.Empty) + ".",
                    Evidence: new CheckInEvidenceBlock
                    {
                        Predicate = label,
                        FactCount = p.Facts.Count,
                        ArtifactCount = p.Artifacts,
                        Examples = examples,
                    });
            });
    }

    /// <summary>
    /// Where a Background answer can record the storyline: the Characters and Factions it is
    /// linked to, then the ones its name, summary or facts mention by name. The witch's warning
    /// names Koves without a relationship, and the merge needs a target.
    /// </summary>
    private static List<CheckInRelatedEntry> RelatedEntries(
        Artifact storyline,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<ArtifactRelationship> relationships,
        IReadOnlyDictionary<Guid, Artifact> artifactsById)
    {
        var related = new List<CheckInRelatedEntry>();
        var seen = new HashSet<Guid>();

        foreach (var relationship in relationships)
        {
            var otherId = relationship.ArtifactAId == storyline.Id ? relationship.ArtifactBId : relationship.ArtifactAId;
            if (artifactsById.TryGetValue(otherId, out var other) && IsPerson(other) && seen.Add(other.Id))
            {
                related.Add(new CheckInRelatedEntry(other.Id, other.Name, other.Type.ToString()));
            }
        }

        var text = string.Join('\n', new[] { storyline.Name, storyline.Summary ?? string.Empty }
            .Concat(facts.Select(f => f.Value)));
        foreach (var other in artifactsById.Values.Where(a => IsPerson(a) && a.Name.Trim().Length >= 3))
        {
            if (!seen.Contains(other.Id) && MentionedIn(text, other.Name) && seen.Add(other.Id))
            {
                related.Add(new CheckInRelatedEntry(other.Id, other.Name, other.Type.ToString()));
            }
        }

        return related;
    }

    private static bool IsPerson(Artifact artifact) =>
        artifact.Type is ArtifactType.Character or ArtifactType.Faction && artifact.Status != ArtifactStatus.Archived;

    private static bool MentionedIn(string text, string name) =>
        Regex.IsMatch(text, @"(?<!\w)" + Regex.Escape(name.Trim()) + @"(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The grouping key for a predicate: trimmed, whitespace collapsed, case-folded.</summary>
    public static string PredicateKey(string predicate) =>
        Regex.Replace(predicate.Trim(), @"\s+", " ").ToLowerInvariant();

    private static List<string> FactLines(IReadOnlyList<ArtifactFact> facts) => facts
        .OrderByDescending(f => f.UpdatedAt)
        .Take(MaxFactLines)
        .Select(f => $"{f.Predicate}: {f.Value}".Truncate(200, ellipsis: true))
        .ToList();

    private static string Sentence(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }
        var joined = string.Join("; ", parts);
        return char.ToUpperInvariant(joined[0]) + joined[1..] + ".";
    }

    private static string Count(int n, string singular, string? plural = null) =>
        $"{n} {(n == 1 ? singular : plural ?? singular + "s")}";

    private static string DisplayType(ArtifactType type) => type.ToString();
}
