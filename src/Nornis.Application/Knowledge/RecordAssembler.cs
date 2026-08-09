using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Knowledge;

/// <summary>The record as one audience may read it, plus how much of it there was.</summary>
public sealed record AssembledRecord(string Text, int ArtifactCount);

/// <summary>
/// Turns a chosen set of artifacts and sources into the formatted record text an AI pass
/// reads, at one audience's visibility.
///
/// Extracted so the world digest and the campaign recap cannot drift apart on what "the
/// record" means, and — more importantly — so the two rules below are written once. Both
/// are the kind that look like details and leak worlds if a second copy omits them:
///
/// <list type="bullet">
/// <item>A relationship row can be visible while one of its ends is not. Naming that end
/// in a relationship line discloses an artifact the reader was refused.</item>
/// <item>A quote rides on its source's visibility, not its target's. A reference whose
/// source fell out of this audience's view carries text they must not read.</item>
/// </list>
///
/// Callers choose the <em>scope</em> — a whole world, or one campaign's evidenced subset —
/// and this decides what may be said about it.
/// </summary>
public interface IRecordAssembler
{
    Task<AssembledRecord> AssembleAsync(
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<Source> sources,
        VisibilityFilter filter,
        bool includeHiddenTruths,
        CancellationToken ct);
}

public class RecordAssembler : IRecordAssembler
{
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;

    public RecordAssembler(
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository)
    {
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
    }

    public async Task<AssembledRecord> AssembleAsync(
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<Source> sources,
        VisibilityFilter filter,
        bool includeHiddenTruths,
        CancellationToken ct)
    {
        var artifactIds = artifacts.Select(a => a.Id).ToList();
        var visibleIds = artifactIds.ToHashSet();

        var facts = (await _artifactFactRepository.ListByArtifactIdsAsync(
                artifactIds, filter, ContinuityAuditService.MaxFactsPerArtifactInAudit, ct))
            .Where(f => f.TruthState != TruthState.False)
            .Where(f => includeHiddenTruths || f.TruthState != TruthState.Hidden)
            .ToList();

        var relationships = (await _artifactRelationshipRepository.ListByArtifactIdsAsync(artifactIds, filter, ct))
            .Where(r => r.TruthState != TruthState.False)
            .Where(r => includeHiddenTruths || r.TruthState != TruthState.Hidden)
            .Where(r => visibleIds.Contains(r.ArtifactAId) && visibleIds.Contains(r.ArtifactBId))
            .ToList();

        var targetIds = artifactIds
            .Concat(facts.Select(f => f.Id))
            .Concat(relationships.Select(r => r.Id))
            .ToList();
        var references = await _sourceReferenceRepository.ListByTargetIdsAsync(targetIds, ct);

        var visibleSourceIds = sources.Select(s => s.Id).ToHashSet();
        references = references.Where(r => visibleSourceIds.Contains(r.SourceId)).ToList();

        var text = ContinuityAuditService.FormatWorldRecord(artifacts, facts, relationships, references, sources);
        return new AssembledRecord(text, artifacts.Count);
    }
}
