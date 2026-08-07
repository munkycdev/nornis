using System.Text;
using Microsoft.Extensions.Logging;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Reveal promotes a GM-curated set of GM-only knowledge to the party. It reuses the review
/// machinery wholesale: every change is an accepted <c>Update*</c> proposal on a synthetic,
/// party-visible reveal source (so the applicator flips visibility <em>and</em> stamps
/// player-visible provenance), applied through the real <see cref="IProposalApplicator"/> in a
/// single transaction — the same confirm-and-apply shape as
/// <see cref="ArtifactMergeService"/>. It never lowers visibility and never touches
/// <c>Private</c> knowledge.
/// </summary>
public class RevealService : IRevealService
{
    private const string RevealVisibilityJson = """{"visibility":"PartyVisible"}""";

    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly SyntheticBatchWriter _batchWriter;
    private readonly ILogger<RevealService> _logger;

    // Optional so existing constructions keep compiling; hosts register them. Used only to
    // check off the demo tutorial's reveal step (feature 20) — reveals work without them.
    private readonly IWorldRepository? _worldRepository;
    private readonly ITutorialProgressRepository? _tutorialProgressRepository;

    public RevealService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceRepository sourceRepository,
        SyntheticBatchWriter batchWriter,
        ILogger<RevealService> logger,
        IWorldRepository? worldRepository = null,
        ITutorialProgressRepository? tutorialProgressRepository = null)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceRepository = sourceRepository;
        _batchWriter = batchWriter;
        _logger = logger;
        _worldRepository = worldRepository;
        _tutorialProgressRepository = tutorialProgressRepository;
    }

    public async Task<AppResult<RevealResult>> RevealAsync(RevealCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return Fail(403, "insufficient_role", "Only GMs can reveal knowledge.");
        }

        // Current visibility of every artifact the reveal touches — directly, or as a fact's
        // parent or a relationship's endpoint. Drives the closure check.
        var knownArtifactVisibility = new Dictionary<Guid, VisibilityScope>();

        async Task<AppError?> RecordArtifactVisibilityAsync(Guid artifactId)
        {
            if (knownArtifactVisibility.ContainsKey(artifactId))
            {
                return null;
            }

            var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
            if (artifact is null || artifact.WorldId != command.WorldId)
            {
                return new AppError(404, "not_found", $"Artifact {artifactId} not found in this world.");
            }

            knownArtifactVisibility[artifactId] = artifact.Visibility;
            return null;
        }

        // Artifacts to promote (GMOnly only; already-PartyVisible are no-ops, Private rejected).
        var artifactsToReveal = new List<Artifact>();
        foreach (var id in command.ArtifactIds.Distinct())
        {
            var artifact = await _artifactRepository.GetByIdAsync(id, ct);
            if (artifact is null || artifact.WorldId != command.WorldId)
            {
                return Fail(404, "not_found", $"Artifact {id} not found in this world.");
            }

            knownArtifactVisibility[id] = artifact.Visibility;

            if (PrivateGuard(artifact.Visibility, "artifact", id) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            if (artifact.Visibility == VisibilityScope.GMOnly)
            {
                artifactsToReveal.Add(artifact);
            }
        }

        // Facts to promote; each needs its parent artifact visible (closure).
        var factsToReveal = new List<ArtifactFact>();
        var factParentIds = new List<Guid>();
        foreach (var id in command.FactIds.Distinct())
        {
            var fact = await _factRepository.GetByIdAsync(id, ct);
            if (fact is null)
            {
                return Fail(404, "not_found", $"Fact {id} not found.");
            }

            if (await RecordArtifactVisibilityAsync(fact.ArtifactId) is { } parentError)
            {
                return AppResult<RevealResult>.Fail(parentError);
            }

            if (PrivateGuard(fact.Visibility, "fact", id) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            if (fact.Visibility == VisibilityScope.GMOnly)
            {
                factsToReveal.Add(fact);
                factParentIds.Add(fact.ArtifactId);
            }
        }

        // Relationships to promote; each needs both endpoint artifacts visible (closure).
        var relationshipsToReveal = new List<ArtifactRelationship>();
        foreach (var id in command.RelationshipIds.Distinct())
        {
            var relationship = await _relationshipRepository.GetByIdAsync(id, ct);
            if (relationship is null || relationship.WorldId != command.WorldId)
            {
                return Fail(404, "not_found", $"Relationship {id} not found in this world.");
            }

            if (PrivateGuard(relationship.Visibility, "relationship", id) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            if (relationship.Visibility == VisibilityScope.GMOnly)
            {
                relationshipsToReveal.Add(relationship);
                if (await RecordArtifactVisibilityAsync(relationship.ArtifactAId) is { } aError)
                {
                    return AppResult<RevealResult>.Fail(aError);
                }
                if (await RecordArtifactVisibilityAsync(relationship.ArtifactBId) is { } bError)
                {
                    return AppResult<RevealResult>.Fail(bError);
                }
            }
        }

        // Corrections: existing facts to re-truth-state as the reveal supersedes them.
        var corrections = new List<FactCorrection>();
        foreach (var correction in command.Corrections)
        {
            var fact = await _factRepository.GetByIdAsync(correction.FactId, ct);
            if (fact is null)
            {
                return Fail(404, "not_found", $"Fact {correction.FactId} not found.");
            }

            if (await RecordArtifactVisibilityAsync(fact.ArtifactId) is { } parentError)
            {
                return AppResult<RevealResult>.Fail(parentError);
            }

            // Same guard the three promotion steps above apply, and for a stronger reason:
            // those would only widen a Private fact's audience, while a correction rewrites
            // its truth state and files a PartyVisible reveal as the provenance. Reveal is
            // the GM-knowledge instrument; a player's Private fact is not its business.
            if (PrivateGuard(fact.Visibility, "fact", correction.FactId) is { } priv)
            {
                return AppResult<RevealResult>.Fail(priv);
            }

            corrections.Add(correction);
        }

        // Closure: reject an incomplete set whole, returning the missing dependencies so the
        //    GM can confirm the expanded scope — never silently reveal more than asked.
        var missing = RevealClosure.MissingArtifactDependencies(
            artifactsToReveal.Select(a => a.Id).ToList(),
            factParentIds,
            relationshipsToReveal.Select(r => (r.ArtifactAId, r.ArtifactBId)).ToList(),
            knownArtifactVisibility);

        if (missing.Count > 0)
        {
            return AppResult<RevealResult>.Success(new RevealResult(null, 0, 0, 0, 0, missing));
        }

        // Nothing to do (all no-ops) — idempotent success, no batch minted.
        if (artifactsToReveal.Count == 0 && factsToReveal.Count == 0
            && relationshipsToReveal.Count == 0 && corrections.Count == 0)
        {
            return AppResult<RevealResult>.Success(new RevealResult(null, 0, 0, 0, 0, []));
        }

        // Provenance + apply, all in one transaction inside the writer. A reveal's
        // synthetic source is the one the party can see — the reveal IS the party-facing
        // record, which is why it is not the default GMOnly GMNote.
        var specs = new List<SyntheticProposalSpec>();
        specs.AddRange(artifactsToReveal.Select(artifact => RevealSpec(
            ReviewChangeType.UpdateArtifact, ReviewTargetType.Artifact, artifact.Id, RevealVisibilityJson, "Revealed to the party.")));
        specs.AddRange(factsToReveal.Select(fact => RevealSpec(
            ReviewChangeType.UpdateFact, ReviewTargetType.ArtifactFact, fact.Id, RevealVisibilityJson, "Revealed to the party.")));
        specs.AddRange(relationshipsToReveal.Select(relationship => RevealSpec(
            ReviewChangeType.UpdateRelationship, ReviewTargetType.ArtifactRelationship, relationship.Id, RevealVisibilityJson, "Revealed to the party.")));
        specs.AddRange(corrections.Select(correction => RevealSpec(
            ReviewChangeType.UpdateFact, ReviewTargetType.ArtifactFact, correction.FactId,
            $$"""{"truthState":"{{correction.TruthState}}"}""", "Corrected on reveal.")));

        var written = await _batchWriter.WriteAcceptedAsync(
            new SyntheticSourceSpec
            {
                WorldId = command.WorldId,
                ActingUserId = command.ActingUserId,
                Type = SourceType.Reveal,
                Title = $"Reveal — {DateTimeOffset.UtcNow:yyyy-MM-dd}",
                Body = BuildBody(command.Note, artifactsToReveal, factsToReveal.Count, relationshipsToReveal.Count, corrections.Count),
                Visibility = VisibilityScope.PartyVisible,
                // Also composed into the body above, for the ledger to read. Kept here
                // structurally so the player-facing view never has to parse it back out.
                RevealNote = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim()
            },
            ReviewBatchKinds.Reveal,
            specs,
            ct);

        if (!written.IsSuccess)
        {
            return AppResult<RevealResult>.Fail(written.Error!);
        }

        _logger.LogInformation(
            "Reveal applied. WorldId={WorldId}, Artifacts={Artifacts}, Facts={Facts}, Relationships={Relationships}, Corrections={Corrections}, BatchId={BatchId}, User={UserId}",
            command.WorldId, artifactsToReveal.Count, factsToReveal.Count, relationshipsToReveal.Count, corrections.Count, written.Value!.BatchId, command.ActingUserId);

        return AppResult<RevealResult>.Success(new RevealResult(
            written.Value.BatchId, artifactsToReveal.Count, factsToReveal.Count, relationshipsToReveal.Count, corrections.Count, []));
    }

    private static SyntheticProposalSpec RevealSpec(
        ReviewChangeType changeType, ReviewTargetType targetType, Guid targetId, string proposedValueJson, string rationale) => new()
        {
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValueJson = proposedValueJson,
            Rationale = rationale
        };

    public async Task<AppResult<RevealSourceResult>> RevealSourceAsync(
        Guid worldId, Guid sourceId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<RevealSourceResult>.Fail(new AppError(403, "insufficient_role", "Only GMs can reveal a source."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId)
        {
            return AppResult<RevealSourceResult>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        if (source.Visibility == VisibilityScope.PartyVisible)
        {
            return AppResult<RevealSourceResult>.Success(new RevealSourceResult(source, true));
        }

        if (source.Visibility == VisibilityScope.Private)
        {
            return AppResult<RevealSourceResult>.Fail(new AppError(400, "cannot_reveal_private",
                "Cannot reveal a Private source; reveal promotes GM-only material to the party."));
        }

        // GMOnly -> PartyVisible via the scoped write, deliberately bypassing SourceService's
        // post-extraction visibility lock — reveal is the sanctioned way to surface it.
        await _sourceRepository.UpdateVisibilityAsync(sourceId, VisibilityScope.PartyVisible, ct);

        _logger.LogInformation(
            "Source revealed to the party. WorldId={WorldId}, SourceId={SourceId}, User={UserId}",
            worldId, sourceId, actingUserId);

        await RecordTutorialRevealAsync(worldId, actingUserId, ct);

        // The no-tracking entity still carries its pre-reveal visibility; reflect the
        // write so the caller renders what the party now sees.
        source.Visibility = VisibilityScope.PartyVisible;

        return AppResult<RevealSourceResult>.Success(new RevealSourceResult(source, false));
    }

    /// <summary>
    /// Revealing a source is one of the two reveal paths the demo tutorial accepts (the
    /// other, canon reveal, is detected from its Kind="Reveal" batch). Source reveal leaves
    /// no other trace, so the step is recorded here directly. Never fails the reveal.
    /// </summary>
    private async Task RecordTutorialRevealAsync(Guid worldId, Guid actingUserId, CancellationToken ct)
    {
        if (_worldRepository is null || _tutorialProgressRepository is null)
        {
            return;
        }

        try
        {
            var world = await _worldRepository.GetByIdAsync(worldId, ct);
            if (world is not { IsDemo: true, TutorialEnabled: true })
            {
                return;
            }

            var existing = await _tutorialProgressRepository.ListAsync(actingUserId, worldId, ct);
            if (existing.Any(p => p.StepKey == TutorialSteps.RevealSecret))
            {
                return;
            }

            await _tutorialProgressRepository.AddRangeAsync([new TutorialProgress
            {
                Id = Guid.NewGuid(),
                UserId = actingUserId,
                WorldId = worldId,
                StepKey = TutorialSteps.RevealSecret,
                CompletedAt = DateTimeOffset.UtcNow,
            }], ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the tutorial reveal step for world {WorldId}", worldId);
        }
    }

    private static AppError? PrivateGuard(VisibilityScope visibility, string kind, Guid id) =>
        visibility == VisibilityScope.Private
            ? new AppError(400, "cannot_reveal_private",
                $"Cannot reveal a Private {kind} ({id}); reveal promotes GM-only knowledge only.")
            : null;

    private static string BuildBody(
        string? note, IReadOnlyList<Artifact> artifacts, int facts, int relationships, int corrections)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.AppendLine(note.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Revealed to the party:");
        foreach (var artifact in artifacts)
        {
            sb.AppendLine($"- {artifact.Type}: {artifact.Name}");
        }
        if (facts > 0)
        {
            sb.AppendLine($"- {facts} fact(s)");
        }
        if (relationships > 0)
        {
            sb.AppendLine($"- {relationships} relationship(s)");
        }
        if (corrections > 0)
        {
            sb.AppendLine($"- {corrections} correction(s)");
        }

        return sb.ToString().TrimEnd();
    }

    private static AppResult<RevealResult> Fail(int status, string code, string message) =>
        AppResult<RevealResult>.Fail(new AppError(status, code, message));
}
