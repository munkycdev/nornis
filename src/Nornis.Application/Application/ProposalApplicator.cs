using System.Text.Json;
using Nornis.Application.Errors;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Application;

/// <summary>
/// Applies the proposed mutation to the knowledge graph based on proposal ChangeType.
/// </summary>
public class ProposalApplicator : IProposalApplicator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly ISourceRepository _sourceRepository;

    public ProposalApplicator(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        return proposal.ChangeType switch
        {
            ReviewChangeType.CreateArtifact => await ApplyCreateArtifact(proposal, batch, ct),
            ReviewChangeType.UpdateArtifact => await ApplyUpdateArtifact(proposal, batch, ct),
            ReviewChangeType.MergeArtifact => await ApplyMergeArtifact(proposal, batch, ct),
            ReviewChangeType.AddFact => await ApplyAddFact(proposal, batch, ct),
            ReviewChangeType.UpdateFact => await ApplyUpdateFact(proposal, batch, ct),
            ReviewChangeType.AddRelationship => await ApplyAddRelationship(proposal, batch, ct),
            ReviewChangeType.UpdateRelationship => await ApplyUpdateRelationship(proposal, batch, ct),
            _ => AppResult<ApplyResult>.Fail(new AppError(400, "unknown_change_type", $"Unknown change type: {proposal.ChangeType}"))
        };
    }

    private async Task<AppResult<ApplyResult>> ApplyCreateArtifact(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        var payload = Deserialize<CreateArtifactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize CreateArtifact payload."));

        if (!Enum.TryParse<ArtifactType>(payload.Type, ignoreCase: true, out var artifactType))
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_artifact_type", $"Invalid artifact type: {payload.Type}"));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        var now = DateTimeOffset.UtcNow;
        var visibility = ResolveVisibility(payload.Visibility, source);

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = batch.WorldId,
            Type = artifactType,
            Name = payload.Name,
            Summary = payload.Summary,
            Visibility = visibility,
            Confidence = payload.Confidence,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _artifactRepository.CreateAsync(artifact, ct);

        // Update proposal TargetId to the newly created artifact
        proposal.TargetId = artifact.Id;

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, artifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(artifact.Id, SourceReferenceTargetType.Artifact));
    }

    private async Task<AppResult<ApplyResult>> ApplyUpdateArtifact(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "UpdateArtifact requires a TargetId."));

        var payload = Deserialize<UpdateArtifactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize UpdateArtifact payload."));

        var artifact = await _artifactRepository.GetByIdAsync(proposal.TargetId.Value, ct);
        if (artifact is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target artifact not found."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        if (payload.Name is not null)
            artifact.Name = payload.Name;

        if (payload.Summary is not null)
            artifact.Summary = payload.Summary;

        if (payload.Visibility is not null)
        {
            var visibility = ResolveVisibility(payload.Visibility, source);
            artifact.Visibility = visibility;
        }

        if (payload.Confidence is not null)
            artifact.Confidence = payload.Confidence;

        if (payload.Status is not null && Enum.TryParse<ArtifactStatus>(payload.Status, ignoreCase: true, out var status))
            artifact.Status = status;

        artifact.UpdatedAt = DateTimeOffset.UtcNow;

        await _artifactRepository.UpdateAsync(artifact, ct);

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, artifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(artifact.Id, SourceReferenceTargetType.Artifact));
    }

    private async Task<AppResult<ApplyResult>> ApplyMergeArtifact(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "MergeArtifact requires a TargetId."));

        var payload = Deserialize<MergeArtifactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize MergeArtifact payload."));

        var targetArtifact = await _artifactRepository.GetByIdAsync(proposal.TargetId.Value, ct);
        if (targetArtifact is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target artifact not found."));

        var sourceArtifact = await _artifactRepository.GetByIdAsync(payload.SourceArtifactId, ct);
        if (sourceArtifact is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "source_artifact_not_found", "Source artifact for merge not found."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        // Update target artifact fields from payload
        if (payload.Name is not null)
            targetArtifact.Name = payload.Name;

        if (payload.Summary is not null)
            targetArtifact.Summary = payload.Summary;

        if (payload.Visibility is not null)
        {
            var visibility = ResolveVisibility(payload.Visibility, source);
            targetArtifact.Visibility = visibility;
        }

        if (payload.Confidence is not null)
            targetArtifact.Confidence = payload.Confidence;

        targetArtifact.UpdatedAt = DateTimeOffset.UtcNow;

        await _artifactRepository.UpdateAsync(targetArtifact, ct);

        // Reassign facts from source artifact to target artifact
        var sourceFacts = await _artifactFactRepository.ListByArtifactAsync(payload.SourceArtifactId, ct);
        foreach (var fact in sourceFacts)
        {
            fact.ArtifactId = targetArtifact.Id;
            await _artifactFactRepository.UpdateAsync(fact, ct);
        }

        // Reassign relationships from source artifact to target artifact
        var sourceRelationships = await _artifactRelationshipRepository.ListByArtifactAsync(payload.SourceArtifactId, ct);
        foreach (var relationship in sourceRelationships)
        {
            // Reassign artifact references
            if (relationship.ArtifactAId == payload.SourceArtifactId)
                relationship.ArtifactAId = targetArtifact.Id;

            if (relationship.ArtifactBId == payload.SourceArtifactId)
                relationship.ArtifactBId = targetArtifact.Id;

            // Remove self-referencing relationships after reassignment
            if (relationship.ArtifactAId == relationship.ArtifactBId)
            {
                // Archive the self-referencing relationship by not updating it
                // (it will be orphaned since the source artifact is archived)
                continue;
            }

            await _artifactRelationshipRepository.UpdateAsync(relationship, ct);
        }

        // Archive the source artifact
        sourceArtifact.Status = ArtifactStatus.Archived;
        sourceArtifact.UpdatedAt = DateTimeOffset.UtcNow;
        await _artifactRepository.UpdateAsync(sourceArtifact, ct);

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, targetArtifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(targetArtifact.Id, SourceReferenceTargetType.Artifact));
    }

    private async Task<AppResult<ApplyResult>> ApplyAddFact(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        var payload = Deserialize<AddFactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize AddFact payload."));

        // Resolve the target artifact: by TargetId, or by name for artifacts created earlier
        // in the same batch (their GUIDs did not exist at extraction time).
        Artifact? artifact;
        if (proposal.TargetId is not null)
        {
            artifact = await _artifactRepository.GetByIdAsync(proposal.TargetId.Value, ct);
            if (artifact is null)
                return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target artifact not found."));
        }
        else if (!string.IsNullOrWhiteSpace(payload.ArtifactName))
        {
            var resolution = await ResolveArtifactByNameAsync(batch.WorldId, payload.ArtifactName, ct);
            if (!resolution.IsSuccess)
                return AppResult<ApplyResult>.Fail(resolution.Error!);
            artifact = resolution.Value!;
        }
        else
        {
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id",
                "AddFact requires a TargetId or an artifactName referencing an Artifact."));
        }

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        var now = DateTimeOffset.UtcNow;
        var visibility = ResolveVisibility(payload.Visibility, source);
        var truthState = ResolveTruthState(payload.TruthState);

        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = payload.Predicate,
            Value = payload.Value,
            Confidence = payload.Confidence,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _artifactFactRepository.CreateAsync(fact, ct);

        // Record the resolved artifact on the proposal so the review trail shows what the
        // name reference resolved to.
        proposal.TargetId ??= artifact.Id;

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactFact, fact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(fact.Id, SourceReferenceTargetType.ArtifactFact));
    }

    private async Task<AppResult<ApplyResult>> ApplyUpdateFact(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "UpdateFact requires a TargetId."));

        var payload = Deserialize<UpdateFactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize UpdateFact payload."));

        var fact = await _artifactFactRepository.GetByIdAsync(proposal.TargetId.Value, ct);
        if (fact is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target fact not found."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        if (payload.Value is not null)
            fact.Value = payload.Value;

        if (payload.Confidence is not null)
            fact.Confidence = payload.Confidence;

        if (payload.TruthState is not null)
            fact.TruthState = ResolveTruthState(payload.TruthState);

        if (payload.Visibility is not null)
        {
            var visibility = ResolveVisibility(payload.Visibility, source);
            fact.Visibility = visibility;
        }

        fact.UpdatedAt = DateTimeOffset.UtcNow;

        await _artifactFactRepository.UpdateAsync(fact, ct);

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactFact, fact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(fact.Id, SourceReferenceTargetType.ArtifactFact));
    }

    private async Task<AppResult<ApplyResult>> ApplyAddRelationship(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        var payload = Deserialize<AddRelationshipPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize AddRelationship payload."));

        // Resolve both endpoints: by id, or by name for artifacts created earlier in the
        // same batch (their GUIDs did not exist at extraction time).
        var endpointA = await ResolveRelationshipEndpointAsync(
            batch.WorldId, payload.ArtifactAId, payload.ArtifactAName, "ArtifactA", ct);
        if (!endpointA.IsSuccess)
            return AppResult<ApplyResult>.Fail(endpointA.Error!);

        var endpointB = await ResolveRelationshipEndpointAsync(
            batch.WorldId, payload.ArtifactBId, payload.ArtifactBName, "ArtifactB", ct);
        if (!endpointB.IsSuccess)
            return AppResult<ApplyResult>.Fail(endpointB.Error!);

        var artifactA = endpointA.Value!;
        var artifactB = endpointB.Value!;

        if (artifactA.Id == artifactB.Id)
            return AppResult<ApplyResult>.Fail(new AppError(400, "self_relationship",
                "A relationship must connect two different artifacts."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        var now = DateTimeOffset.UtcNow;
        var visibility = ResolveVisibility(payload.Visibility, source);
        var truthState = ResolveTruthState(payload.TruthState);

        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = batch.WorldId,
            ArtifactAId = artifactA.Id,
            ArtifactBId = artifactB.Id,
            Type = payload.Type,
            Description = payload.Description,
            Confidence = payload.Confidence,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _artifactRelationshipRepository.CreateAsync(relationship, ct);

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactRelationship, relationship.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(relationship.Id, SourceReferenceTargetType.ArtifactRelationship));
    }

    private async Task<AppResult<ApplyResult>> ApplyUpdateRelationship(
        ReviewProposal proposal, ReviewBatch batch, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "UpdateRelationship requires a TargetId."));

        var payload = Deserialize<UpdateRelationshipPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize UpdateRelationship payload."));

        var relationship = await _artifactRelationshipRepository.GetByIdAsync(proposal.TargetId.Value, ct);
        if (relationship is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target relationship not found."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "source_not_found", "Source associated with batch not found."));

        if (payload.Type is not null)
            relationship.Type = payload.Type;

        if (payload.Description is not null)
            relationship.Description = payload.Description;

        if (payload.Confidence is not null)
            relationship.Confidence = payload.Confidence;

        if (payload.TruthState is not null)
            relationship.TruthState = ResolveTruthState(payload.TruthState);

        if (payload.Visibility is not null)
        {
            var visibility = ResolveVisibility(payload.Visibility, source);
            relationship.Visibility = visibility;
        }

        relationship.UpdatedAt = DateTimeOffset.UtcNow;

        await _artifactRelationshipRepository.UpdateAsync(relationship, ct);

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactRelationship, relationship.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(relationship.Id, SourceReferenceTargetType.ArtifactRelationship));
    }

    /// <summary>
    /// Resolves an artifact by exact name within the world. Fails when the name matches
    /// nothing (the referenced CreateArtifact proposal was rejected or not yet accepted) or
    /// more than one artifact (ambiguous — the reviewer must edit the proposal to use an id).
    /// </summary>
    private async Task<AppResult<Artifact>> ResolveArtifactByNameAsync(
        Guid worldId, string name, CancellationToken ct)
    {
        var matches = await _artifactRepository.ListByExactNameAsync(worldId, name.Trim(), ct);

        return matches.Count switch
        {
            0 => AppResult<Artifact>.Fail(new AppError(404, "artifact_name_not_found",
                $"No artifact named '{name}' exists in this world. If it is proposed in this batch, accept its Create proposal first.")),
            1 => AppResult<Artifact>.Success(matches[0]),
            _ => AppResult<Artifact>.Fail(new AppError(409, "artifact_name_ambiguous",
                $"Multiple artifacts are named '{name}'. Edit the proposal to reference the intended artifact by id."))
        };
    }

    private async Task<AppResult<Artifact>> ResolveRelationshipEndpointAsync(
        Guid worldId, Guid? artifactId, string? artifactName, string endpointLabel, CancellationToken ct)
    {
        if (artifactId is not null && artifactId != Guid.Empty)
        {
            var artifact = await _artifactRepository.GetByIdAsync(artifactId.Value, ct);
            if (artifact is null)
            {
                var code = endpointLabel == "ArtifactA" ? "artifact_a_not_found" : "artifact_b_not_found";
                return AppResult<Artifact>.Fail(new AppError(404, code, $"{endpointLabel} not found."));
            }

            return AppResult<Artifact>.Success(artifact);
        }

        if (!string.IsNullOrWhiteSpace(artifactName))
            return await ResolveArtifactByNameAsync(worldId, artifactName, ct);

        return AppResult<Artifact>.Fail(new AppError(400, "invalid_payload",
            $"AddRelationship: {endpointLabel}Id or {endpointLabel}Name is required."));
    }

    private async Task CreateSourceReference(
        Guid sourceId, SourceReferenceTargetType targetType, Guid targetId, Guid proposalId, CancellationToken ct)
    {
        // Carry the supporting excerpt captured at extraction onto the accepted
        // entity's reference so artifact detail can show it.
        var proposalReferences = await _sourceReferenceRepository.ListByTargetAsync(
            SourceReferenceTargetType.ReviewProposal, proposalId, ct);

        var reference = new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            TargetType = targetType,
            TargetId = targetId,
            Quote = proposalReferences.FirstOrDefault()?.Quote,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sourceReferenceRepository.CreateAsync(reference, ct);
    }

    private static VisibilityScope ResolveVisibility(string? proposedVisibility, Source source)
    {
        if (proposedVisibility is not null &&
            Enum.TryParse<VisibilityScope>(proposedVisibility, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return source.Visibility;
    }

    private static TruthState ResolveTruthState(string? truthStateStr)
    {
        if (truthStateStr is not null &&
            Enum.TryParse<TruthState>(truthStateStr, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return TruthState.Likely;
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
