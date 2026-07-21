using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Adds an AI-assessed tier to Continuity Health. The heuristic <see cref="IHealthService"/> stays
/// the fast/free score; this service reads the whole (GM-scoped) record with an LLM to name
/// specific semantic risks — contradictions, dangling threads, dormant load-bearing storylines,
/// timeline conflicts, and summary drift — then blends the two deterministically (the LLM never
/// grades the score). Every finding is grounded in cited item ids; ungrounded ones are dropped.
/// </summary>
public class ContinuityAuditService : IContinuityAuditService
{
    private readonly IHealthService _healthService;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IAuditAiClient _aiClient;
    private readonly IHealthAssessmentRepository _assessmentRepository;
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly LoremasterOptions _options;

    /// <summary>Findings accepted per assessment are capped to keep the report actionable.</summary>
    public const int MaxFindings = 20;

    /// <summary>
    /// Prompt-size guards: the audit reads the whole record in one request, and a grown world
    /// can outrun the AI deployment's tokens-per-minute quota. Archived artifacts are skipped
    /// outright (merge leftovers), facts cap per artifact, and quotes cap overall.
    /// </summary>
    public const int MaxFactsPerArtifactInAudit = 25;
    public const int MaxQuotesInAudit = 100;

    public const string SystemPrompt = """
        You are the Continuity Auditor for Nornis, a tabletop RPG world memory system. You read a
        world's structured record — artifacts (characters, locations, items, factions, events,
        storylines, concepts, documents), the facts attached to them, the relationships between them,
        and the source quotes behind them — and you report specific problems with its SEMANTIC
        CONTINUITY. You are not a proofreader and you do not grade quality; you find risks a careful
        GM would want flagged.

        ## What to look for
        - Contradiction: two facts or relationships that cannot both be true as written (e.g. a
          character's location or allegiance stated two incompatible ways).
        - DanglingThread: a promise, hook, or question the record raises but never follows up — an
          item introduced and never used, a threat named and never resolved.
        - StaleStoryline: a Dormant or long-untouched storyline that many active artifacts still
          connect to — load-bearing but going nowhere.
        - TimelineConflict: events whose OccurredAt ordering contradicts what the facts imply.
        - SummaryDrift: an artifact whose summary no longer matches the facts now attached to it.

        ## Grounding rules — non-negotiable
        - Assess ONLY the record provided below. Do not invent problems, entities, or connections.
        - The record shows only LIVE material: facts and relationships that were retired (marked
          False) have already been adjudicated and removed. Do not re-derive a resolved conflict
          from source quotes or old summary phrasing when its losing side no longer appears
          among the facts.
        - Every finding MUST cite one or more evidence ids, copied EXACTLY from the record (the
          bracketed [ref:...] ids). A finding you cannot ground in real ids is not allowed.
        - Set artifactRef to the primary artifact ref id a GM should open to act on the finding,
          when one applies.
        - An empty findings array is a valid and good result. A tidy record has nothing to report.
        - Prefer a few high-signal findings over many weak ones.

        ## Severity
        - High: an outright contradiction or a broken load-bearing thread that will confuse play.
        - Medium: a real gap or drift worth a GM's attention but not urgent.
        - Low: a minor loose end.

        ## Output
        Respond with a JSON object matching the schema: a "findings" array (0 to 20 items). Each
        finding has category (one of Contradiction, DanglingThread, StaleStoryline, TimelineConflict,
        SummaryDrift), severity (High, Medium, Low), summary (one sentence), suggestedAction (a short
        concrete next step, or null), evidence (array of ref ids from the record), and artifactRef
        (a single ref id or null).
        """;

    private readonly IAiBudgetGuard _budgetGuard;

    public ContinuityAuditService(
        IAiBudgetGuard budgetGuard,
        IHealthService healthService,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository,
        IAuditAiClient aiClient,
        IHealthAssessmentRepository assessmentRepository,
        IAiUsageRecordRepository aiUsageRecordRepository,
        IOptions<LoremasterOptions> options)
    {
        _budgetGuard = budgetGuard;
        _healthService = healthService;
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
        _aiClient = aiClient;
        _assessmentRepository = assessmentRepository;
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _options = options.Value;
    }

    public async Task<AppResult<ContinuityAssessment>> RunAssessmentAsync(
        Guid worldId, Guid? userId, CancellationToken ct)
    {
        // 0. Daily AI budget gate — the audit reads the whole record into a prompt.
        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
            return AppResult<ContinuityAssessment>.Fail(budgetError);

        // 1. Heuristic base score (the fast/free tier we blend against).
        var heuristicResult = await _healthService.GetHealthAsync(worldId, ct);
        var heuristic = heuristicResult.IsSuccess ? heuristicResult.Value!.OverallScore : 0;

        // 2. Load the full GM-scoped record. Archived artifacts (merge leftovers) are dead
        // weight for a continuity read and stay out of the prompt.
        var artifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, null, ct))
            .Where(a => a.Status != ArtifactStatus.Archived)
            .ToList();
        var artifactIds = artifacts.Select(a => a.Id).ToList();

        // Retired (False) facts and relationships stay out of the prompt for the same reason
        // archived artifacts do: they are adjudicated history, and showing them makes the
        // auditor re-report every contradiction whose losing side was already retired.
        var facts = artifactIds.Count > 0
            ? (await _factRepository.ListByArtifactIdsAsync(artifactIds, VisibilityFilter.All, MaxFactsPerArtifactInAudit, ct))
                .Where(f => f.TruthState != TruthState.False)
                .ToList()
            : [];
        var relationships = artifactIds.Count > 0
            ? (await _relationshipRepository.ListByArtifactIdsAsync(artifactIds, VisibilityFilter.All, ct))
                .Where(r => r.TruthState != TruthState.False)
                .ToList()
            : [];

        var targetIds = new List<Guid>(artifactIds);
        targetIds.AddRange(facts.Select(f => f.Id));
        targetIds.AddRange(relationships.Select(r => r.Id));
        var sourceRefs = targetIds.Count > 0
            ? await _sourceReferenceRepository.ListByTargetIdsAsync(targetIds, ct)
            : [];
        var sources = await _sourceRepository.ListByWorldAsync(worldId, null, ct);

        var recordText = FormatWorldRecord(artifacts, facts, relationships, sourceRefs, sources);
        var recordLookup = BuildRecordLookup(artifacts, facts, relationships);

        // 3. Call the AI. Track usage on success and failure alike (parity with LoremasterService).
        var request = new AuditAiRequest
        {
            SystemPrompt = SystemPrompt,
            UserMessage = recordText,
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

        AuditAiResponse response;
        try
        {
            response = await _aiClient.AssessAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await TrackUsageAsync(worldId, userId, null, false, "ServiceError", ct);
            return AppResult<ContinuityAssessment>.Fail(
                new AppError(503, "service_unavailable",
                    "The continuity auditor is temporarily unavailable. Please try again."));
        }

        await TrackUsageAsync(worldId, userId, response, true, null, ct);

        // 4. Validate, carry adjudications forward, persist. A dismissal is a GM decision about
        // an issue, not about one assessment run — when the model re-detects a finding the GM
        // already dismissed, it arrives dismissed instead of re-opening the argument.
        var findings = BuildValidatedFindings(response.Findings, artifacts, facts, relationships);

        var previous = await _assessmentRepository.GetLatestWithFindingsAsync(worldId, ct);
        CarryForwardDismissals(findings, previous?.Findings ?? []);

        var assessment = new HealthAssessment
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CreatedAt = DateTimeOffset.UtcNow,
            Model = response.Model,
            Score = BlendScore(heuristic, findings
                .Where(f => f.Status == ContinuityFindingStatus.Open)
                .Select(f => f.Severity)),
        };
        foreach (var f in findings)
        {
            f.HealthAssessmentId = assessment.Id;
        }

        await _assessmentRepository.CreateAsync(assessment, findings, ct);

        // At creation nothing is stale, so effective == snapshot.
        return AppResult<ContinuityAssessment>.Success(
            ToAssessment(assessment, findings, heuristic, recordLookup));
    }

    public async Task<AppResult<ContinuityAssessment>> GetLatestAsync(Guid worldId, CancellationToken ct)
    {
        var assessment = await _assessmentRepository.GetLatestWithFindingsAsync(worldId, ct);
        if (assessment is null)
        {
            return AppResult<ContinuityAssessment>.Success(
                new ContinuityAssessment(false, null, null, null, 0, 0, 0, []));
        }

        // Effective score uses the current heuristic (always fresh/free) minus penalties for the
        // findings that are still Open and still grounded — dismissing a finding, or editing the
        // evidence it cites (which makes it stale), raises the effective score.
        var heuristicResult = await _healthService.GetHealthAsync(worldId, ct);
        var heuristic = heuristicResult.IsSuccess ? heuristicResult.Value!.OverallScore : 0;

        var recordLookup = await LoadRecordLookupAsync(assessment.Findings, ct);

        return AppResult<ContinuityAssessment>.Success(
            ToAssessment(assessment, assessment.Findings, heuristic, recordLookup));
    }

    public async Task<AppResult<ContinuityFindingView>> DismissFindingAsync(
        Guid worldId, Guid findingId, CancellationToken ct)
    {
        var finding = await _assessmentRepository.GetFindingByIdAsync(findingId, ct);
        if (finding is null || finding.HealthAssessment.WorldId != worldId)
        {
            return AppResult<ContinuityFindingView>.Fail(
                new AppError(404, "not_found", "Finding not found."));
        }

        if (finding.Status == ContinuityFindingStatus.Open)
        {
            finding.Status = ContinuityFindingStatus.Dismissed;
            finding = await _assessmentRepository.UpdateFindingAsync(finding, ct);
        }

        var recordLookup = await LoadRecordLookupAsync([finding], ct);

        return AppResult<ContinuityFindingView>.Success(
            ToFindingView(finding, finding.HealthAssessment.CreatedAt, recordLookup));
    }

    // ---------------------------------------------------------------- Scoring (deterministic) --

    /// <summary>Penalty a single open finding contributes, by severity.</summary>
    public static int PenaltyFor(ContinuityFindingSeverity severity) => severity switch
    {
        ContinuityFindingSeverity.High => 12,
        ContinuityFindingSeverity.Medium => 6,
        ContinuityFindingSeverity.Low => 2,
        _ => 0
    };

    /// <summary>Total severity-weighted penalty for the open findings, capped at 40.</summary>
    public static int TotalPenalty(IEnumerable<ContinuityFindingSeverity> openSeverities) =>
        Math.Min(40, openSeverities.Sum(PenaltyFor));

    /// <summary>Blended score: heuristic minus capped penalty, floored at 0.</summary>
    public static int BlendScore(int heuristic, IEnumerable<ContinuityFindingSeverity> openSeverities) =>
        Math.Max(0, heuristic - TotalPenalty(openSeverities));

    /// <summary>
    /// Marks new findings Dismissed when the previous assessment holds a dismissed finding of
    /// the same category whose evidence covers the new finding's (new evidence ⊆ dismissed
    /// evidence). A re-detection citing anything beyond what was dismissed is new information
    /// and stays Open.
    /// </summary>
    internal static void CarryForwardDismissals(
        IReadOnlyList<ContinuityFinding> findings,
        IEnumerable<ContinuityFinding> previousFindings)
    {
        var dismissed = previousFindings
            .Where(f => f.Status == ContinuityFindingStatus.Dismissed)
            .Select(f => (f.Category, Evidence: DeserializeEvidence(f.EvidenceJson).ToHashSet(StringComparer.Ordinal)))
            .Where(d => d.Evidence.Count > 0)
            .ToList();
        if (dismissed.Count == 0)
            return;

        foreach (var finding in findings)
        {
            var evidence = DeserializeEvidence(finding.EvidenceJson);
            if (dismissed.Any(d => d.Category == finding.Category && evidence.All(d.Evidence.Contains)))
            {
                finding.Status = ContinuityFindingStatus.Dismissed;
            }
        }
    }

    // -------------------------------------------------------------------------- Validation --

    /// <summary>
    /// Turns raw AI findings into persistable entities: invalid category/severity are dropped,
    /// evidence ids that don't resolve to real world items are stripped, findings left with no
    /// grounding are dropped entirely (mirroring ParseCitations), and the total is capped.
    /// </summary>
    internal static List<ContinuityFinding> BuildValidatedFindings(
        IReadOnlyList<AuditFinding> raw,
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<ArtifactRelationship> relationships)
    {
        var artifactRefs = artifacts.ToDictionary(a => ArtifactRef(a.Id), a => a.Id);
        var factRefs = facts.ToDictionary(f => FactRef(f.Id), f => f);
        var relRefs = relationships.Select(r => RelRef(r.Id)).ToHashSet(StringComparer.Ordinal);

        bool Resolves(string refId) =>
            artifactRefs.ContainsKey(refId) || factRefs.ContainsKey(refId) || relRefs.Contains(refId);

        var result = new List<ContinuityFinding>();

        foreach (var f in raw)
        {
            if (!Enum.TryParse<ContinuityFindingCategory>(f.Category, ignoreCase: true, out var category))
                continue;
            if (!Enum.TryParse<ContinuityFindingSeverity>(f.Severity, ignoreCase: true, out var severity))
                continue;
            if (string.IsNullOrWhiteSpace(f.Summary))
                continue;

            // Keep only evidence ids that resolve; an ungrounded finding is discarded.
            var evidence = (f.Evidence ?? [])
                .Select(NormalizeRefId)
                .Where(Resolves)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (evidence.Count == 0)
                continue;

            var artifactId = ResolveArtifactId(
                f.ArtifactRef is null ? null : NormalizeRefId(f.ArtifactRef),
                evidence, artifactRefs, factRefs);

            result.Add(new ContinuityFinding
            {
                Id = Guid.NewGuid(),
                Category = category,
                Severity = severity,
                Summary = f.Summary.Trim(),
                SuggestedAction = string.IsNullOrWhiteSpace(f.SuggestedAction) ? null : f.SuggestedAction.Trim(),
                EvidenceJson = JsonSerializer.Serialize(evidence),
                ArtifactId = artifactId,
                Status = ContinuityFindingStatus.Open
            });

            if (result.Count >= MaxFindings)
                break;
        }

        return result;
    }

    /// <summary>
    /// The record renders ids as "[ref:fact:GUID]" and the prompt tells the model to copy them
    /// exactly, so responses reliably echo the "ref:" prefix (and occasionally the brackets).
    /// Lookups are keyed on the bare "kind:guid" form — normalize before resolving.
    /// </summary>
    internal static string NormalizeRefId(string refId)
    {
        var id = refId.Trim().TrimStart('[').TrimEnd(']').Trim();
        if (id.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            id = id[4..];
        return id;
    }

    private static Guid? ResolveArtifactId(
        string? artifactRef,
        IReadOnlyList<string> evidence,
        Dictionary<string, Guid> artifactRefs,
        Dictionary<string, ArtifactFact> factRefs)
    {
        // Prefer the model's explicit primary artifact if it resolves.
        if (artifactRef is not null && artifactRefs.TryGetValue(artifactRef, out var direct))
            return direct;

        // Otherwise fall back to the first artifact cited in evidence, then the artifact owning
        // the first cited fact — whichever the GM would most usefully land on.
        foreach (var e in evidence)
        {
            if (artifactRefs.TryGetValue(e, out var a))
                return a;
        }
        foreach (var e in evidence)
        {
            if (factRefs.TryGetValue(e, out var fact))
                return fact.ArtifactId;
        }
        return null;
    }

    // ------------------------------------------------------------------ Evidence resolution --

    /// <summary>
    /// The world items evidence refs resolve against, keyed by id. Built in-memory during a run
    /// (the record is already loaded) and loaded by cited ids everywhere else.
    /// </summary>
    internal sealed record RecordLookup(
        IReadOnlyDictionary<Guid, Artifact> Artifacts,
        IReadOnlyDictionary<Guid, ArtifactFact> Facts,
        IReadOnlyDictionary<Guid, ArtifactRelationship> Relationships);

    internal static RecordLookup BuildRecordLookup(
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<ArtifactRelationship> relationships) => new(
        artifacts.ToDictionary(a => a.Id),
        facts.ToDictionary(f => f.Id),
        relationships.ToDictionary(r => r.Id));

    /// <summary>
    /// Loads just the items the given findings cite (plus the artifacts that own or anchor
    /// them, for labels and navigation) — a handful of by-id lookups, not a record re-read.
    /// </summary>
    private async Task<RecordLookup> LoadRecordLookupAsync(
        IEnumerable<ContinuityFinding> findings, CancellationToken ct)
    {
        var artifactIds = new HashSet<Guid>();
        var factIds = new HashSet<Guid>();
        var relIds = new HashSet<Guid>();

        foreach (var finding in findings)
        {
            foreach (var refId in DeserializeEvidence(finding.EvidenceJson))
            {
                if (!TryParseRef(refId, out var kind, out var id))
                    continue;
                switch (kind)
                {
                    case "artifact": artifactIds.Add(id); break;
                    case "fact": factIds.Add(id); break;
                    case "rel": relIds.Add(id); break;
                }
            }
        }

        var facts = await _factRepository.ListByIdsAsync([.. factIds], ct);
        var relationships = await _relationshipRepository.ListByIdsAsync([.. relIds], ct);

        // Owning/endpoint artifacts ride along so fact and relationship labels can be named.
        foreach (var f in facts)
            artifactIds.Add(f.ArtifactId);
        foreach (var r in relationships)
        {
            artifactIds.Add(r.ArtifactAId);
            artifactIds.Add(r.ArtifactBId);
        }

        var artifacts = await _artifactRepository.ListByIdsAsync([.. artifactIds], ct);

        return BuildRecordLookup(artifacts, facts, relationships);
    }

    /// <summary>Splits a normalized "kind:guid" evidence ref into its parts.</summary>
    internal static bool TryParseRef(string refId, out string kind, out Guid id)
    {
        kind = string.Empty;
        id = Guid.Empty;

        var separator = refId.IndexOf(':');
        if (separator <= 0)
            return false;
        if (!Guid.TryParse(refId[(separator + 1)..], out id))
            return false;

        kind = refId[..separator].ToLowerInvariant();
        return true;
    }

    internal static ContinuityEvidenceItemView BuildEvidenceItem(
        string refId, DateTimeOffset assessedAt, RecordLookup lookup)
    {
        if (!TryParseRef(refId, out var kind, out var id))
            return new ContinuityEvidenceItemView(refId, "Item", "Unrecognized reference", null, false, true);

        switch (kind)
        {
            case "artifact" when lookup.Artifacts.TryGetValue(id, out var artifact):
                return new ContinuityEvidenceItemView(
                    refId, "Artifact", artifact.Name, artifact.Id,
                    artifact.UpdatedAt > assessedAt, false);

            case "artifact":
                return new ContinuityEvidenceItemView(
                    refId, "Artifact", "No longer in the record", null, false, true);

            case "fact" when lookup.Facts.TryGetValue(id, out var fact):
                var owner = lookup.Artifacts.GetValueOrDefault(fact.ArtifactId);
                return new ContinuityEvidenceItemView(
                    refId, "Fact",
                    $"{owner?.Name ?? "Unknown artifact"} — {fact.Predicate}: {Truncate(fact.Value, 80)}",
                    fact.ArtifactId, fact.UpdatedAt > assessedAt, false);

            case "fact":
                return new ContinuityEvidenceItemView(
                    refId, "Fact", "No longer in the record", null, false, true);

            case "rel" when lookup.Relationships.TryGetValue(id, out var rel):
                var a = lookup.Artifacts.GetValueOrDefault(rel.ArtifactAId)?.Name ?? "Unknown artifact";
                var b = lookup.Artifacts.GetValueOrDefault(rel.ArtifactBId)?.Name ?? "Unknown artifact";
                return new ContinuityEvidenceItemView(
                    refId, "Relationship", $"{a} ↔ {b} — {rel.Type}", rel.ArtifactAId,
                    rel.UpdatedAt > assessedAt, false);

            case "rel":
                return new ContinuityEvidenceItemView(
                    refId, "Relationship", "No longer in the record", null, false, true);

            default:
                return new ContinuityEvidenceItemView(refId, "Item", "Unrecognized reference", null, false, true);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    // ---------------------------------------------------------------------------- Mapping --

    private static ContinuityAssessment ToAssessment(
        HealthAssessment assessment, IEnumerable<ContinuityFinding> findings, int heuristic,
        RecordLookup lookup)
    {
        var list = findings.ToList();
        var views = list.Select(f => ToFindingView(f, assessment.CreatedAt, lookup)).ToList();

        // Stale findings stop counting: their cited evidence changed after the audit ran, so
        // the claim is unverified until the next run. The GM acted on exactly the items the
        // finding named — the score should respond the way a dismissal does.
        var countedSeverities = list.Zip(views)
            .Where(p => p.First.Status == ContinuityFindingStatus.Open && !p.Second.IsStale)
            .Select(p => p.First.Severity);

        return new ContinuityAssessment(
            HasData: true,
            AssessmentId: assessment.Id,
            CreatedAt: assessment.CreatedAt,
            Model: assessment.Model,
            Score: assessment.Score,
            EffectiveScore: BlendScore(heuristic, countedSeverities),
            HeuristicScore: heuristic,
            Findings: views);
    }

    private static ContinuityFindingView ToFindingView(
        ContinuityFinding f, DateTimeOffset assessedAt, RecordLookup lookup)
    {
        var evidence = DeserializeEvidence(f.EvidenceJson);
        var items = evidence
            .Select(refId => BuildEvidenceItem(refId, assessedAt, lookup))
            .ToList();

        return new ContinuityFindingView(
            f.Id,
            f.Category.ToString(),
            f.Severity.ToString(),
            f.Summary,
            f.SuggestedAction,
            evidence,
            items,
            f.ArtifactId,
            f.Status.ToString(),
            IsStale: items.Any(i => i.ChangedSinceAudit || i.Missing));
    }

    private static IReadOnlyList<string> DeserializeEvidence(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ------------------------------------------------------------------------- Formatting --

    private static string ArtifactRef(Guid id) => $"artifact:{id}";
    private static string FactRef(Guid id) => $"fact:{id}";
    private static string RelRef(Guid id) => $"rel:{id}";

    /// <summary>
    /// Renders the full record for the prompt: artifacts with type/status/summary/timestamps,
    /// their facts (with truth state), relationships with endpoint names, a source-quote list, and
    /// an OccurredAt timeline — each item tagged with the [ref:...] id the AI must cite.
    /// </summary>
    internal static string FormatWorldRecord(
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<ArtifactFact> facts,
        IReadOnlyList<ArtifactRelationship> relationships,
        IReadOnlyList<SourceReference> sourceRefs,
        IReadOnlyList<Source> sources)
    {
        var sb = new StringBuilder();
        var names = artifacts.ToDictionary(a => a.Id, a => a.Name);
        var factsByArtifact = facts.GroupBy(f => f.ArtifactId).ToDictionary(g => g.Key, g => g.ToList());

        sb.AppendLine("# World Record");
        sb.AppendLine();

        sb.AppendLine("## Artifacts");
        if (artifacts.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        foreach (var a in artifacts)
        {
            sb.AppendLine(
                $"- {a.Name} ({a.Type}, {a.Status}): {a.Summary ?? "No summary"} " +
                $"[created {Stamp(a.CreatedAt)}, updated {Stamp(a.UpdatedAt)}] [ref:{ArtifactRef(a.Id)}]");

            if (factsByArtifact.TryGetValue(a.Id, out var artifactFacts))
            {
                foreach (var f in artifactFacts)
                {
                    sb.AppendLine(
                        $"    - {f.Predicate}: {f.Value} [{f.TruthState}] " +
                        $"[updated {Stamp(f.UpdatedAt)}] [ref:{FactRef(f.Id)}]");
                }
            }
        }
        sb.AppendLine();

        var orphanFacts = facts.Where(f => !names.ContainsKey(f.ArtifactId)).ToList();
        if (orphanFacts.Count > 0)
        {
            sb.AppendLine("## Additional Facts");
            foreach (var f in orphanFacts)
            {
                sb.AppendLine($"- {f.Predicate}: {f.Value} [{f.TruthState}] [ref:{FactRef(f.Id)}]");
            }
            sb.AppendLine();
        }

        if (relationships.Count > 0)
        {
            sb.AppendLine("## Relationships");
            foreach (var r in relationships)
            {
                var an = names.GetValueOrDefault(r.ArtifactAId, "Unknown artifact");
                var bn = names.GetValueOrDefault(r.ArtifactBId, "Unknown artifact");
                var desc = r.Description is not null ? $" — {r.Description}" : "";
                sb.AppendLine($"- {an} <-> {bn}: {r.Type}{desc} [{r.TruthState}] [ref:{RelRef(r.Id)}]");
            }
            sb.AppendLine();
        }

        var quoted = sourceRefs
            .Where(s => !string.IsNullOrWhiteSpace(s.Quote))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        if (quoted.Count > 0)
        {
            sb.AppendLine("## Source Quotes");
            foreach (var s in quoted.Take(MaxQuotesInAudit))
            {
                sb.AppendLine($"- \"{s.Quote}\"");
            }
            if (quoted.Count > MaxQuotesInAudit)
            {
                sb.AppendLine($"(+{quoted.Count - MaxQuotesInAudit} older quotes omitted)");
            }
            sb.AppendLine();
        }

        var dated = sources.Where(s => s.OccurredAt.HasValue).OrderBy(s => s.OccurredAt).ToList();
        if (dated.Count > 0)
        {
            sb.AppendLine("## Timeline (source events by OccurredAt)");
            foreach (var s in dated)
            {
                sb.AppendLine($"- {Stamp(s.OccurredAt!.Value)}: {s.Title}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Stamp(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // --------------------------------------------------------------------- Usage tracking --

    private async Task TrackUsageAsync(
        Guid worldId, Guid? userId, AuditAiResponse? response, bool succeeded, string? errorCode,
        CancellationToken ct)
    {
        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            OperationType = AiOperationType.ContinuityAudit,
            Model = response?.Model ?? _options.AiModel,
            InputTokens = response?.InputTokens ?? 0,
            OutputTokens = response?.OutputTokens ?? 0,
            TotalTokens = response?.TotalTokens ?? 0,
            EstimatedCostUsd = CalculateCost(response),
            DurationMs = response?.DurationMs ?? 0,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _aiUsageRecordRepository.CreateAsync(record, ct);
    }

    private decimal CalculateCost(AuditAiResponse? response)
    {
        if (response is null)
            return 0m;

        // Cost lookup keys on the model the response reports — same discipline as LoremasterService.
        if (!_options.ModelPricing.TryGetValue(response.Model, out var pricing))
            return 0m;

        var inputCost = response.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m;
        var outputCost = response.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        return inputCost + outputCost;
    }
}
