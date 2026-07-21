using System.Text.Json;
using Nornis.Application.Errors;
using Nornis.Application.Services;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IMapPlacemarkRepository _mapPlacemarkRepository;

    public ProposalApplicator(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IMapPlacemarkRepository mapPlacemarkRepository)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _mapPlacemarkRepository = mapPlacemarkRepository;
    }

    public async Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal, ReviewBatch batch, VisibilityFilter actingFilter, CancellationToken ct)
    {
        return proposal.ChangeType switch
        {
            ReviewChangeType.CreateArtifact => await ApplyCreateArtifact(proposal, batch, ct),
            ReviewChangeType.UpdateArtifact => await ApplyUpdateArtifact(proposal, batch, ct),
            ReviewChangeType.MergeArtifact => await ApplyMergeArtifact(proposal, batch, ct),
            ReviewChangeType.AddFact => await ApplyAddFact(proposal, batch, actingFilter, ct),
            ReviewChangeType.UpdateFact => await ApplyUpdateFact(proposal, batch, ct),
            ReviewChangeType.AddRelationship => await ApplyAddRelationship(proposal, batch, actingFilter, ct),
            ReviewChangeType.UpdateRelationship => await ApplyUpdateRelationship(proposal, batch, ct),
            ReviewChangeType.AddPlacemark => await ApplyAddPlacemark(proposal, batch, actingFilter, ct),
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
            // Owner = the source's author: Private knowledge stays with whoever wrote it.
            CreatedByUserId = source.CreatedByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _artifactRepository.CreateAsync(artifact, ct);

        // Map-extracted locations carry a pin block: one accept creates the artifact
        // AND its placemark. A bad block fails the apply — the accept transaction rolls
        // the artifact back rather than leaving a pinless half-accept.
        if (payload.MapPlacemark is { } pin)
        {
            var pinError = await CreatePlacemarkAsync(batch, artifact.Id, pin.AttachmentId, pin.X, pin.Y, pin.Label, payload.Confidence, ct);
            if (pinError is not null)
                return AppResult<ApplyResult>.Fail(pinError);
        }

        // Update proposal TargetId to the newly created artifact
        proposal.TargetId = artifact.Id;

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, artifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(artifact.Id, SourceReferenceTargetType.Artifact));
    }

    private async Task<AppResult<ApplyResult>> ApplyAddPlacemark(
        ReviewProposal proposal, ReviewBatch batch, VisibilityFilter actingFilter, CancellationToken ct)
    {
        var payload = Deserialize<AddPlacemarkPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize AddPlacemark payload."));

        // Resolve the artifact: TargetId, payload id, or name (ambiguity surfaces to the
        // reviewer exactly like name-referenced facts).
        Artifact? artifact;
        var artifactId = proposal.TargetId ?? payload.ArtifactId;
        if (artifactId is not null && artifactId != Guid.Empty)
        {
            artifact = await _artifactRepository.GetByIdAsync(artifactId.Value, ct);
            if (artifact is null || artifact.WorldId != batch.WorldId)
                return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target artifact not found."));
        }
        else if (!string.IsNullOrWhiteSpace(payload.ArtifactName))
        {
            var resolution = await ResolveArtifactByNameAsync(batch.WorldId, payload.ArtifactName, actingFilter, ct);
            if (!resolution.IsSuccess)
                return AppResult<ApplyResult>.Fail(resolution.Error!);
            artifact = resolution.Value!;
        }
        else
        {
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload",
                "AddPlacemark requires an ArtifactId or ArtifactName."));
        }

        var pinError = await CreatePlacemarkAsync(batch, artifact.Id, payload.AttachmentId, payload.X, payload.Y, payload.Label, payload.Confidence, ct);
        if (pinError is not null)
            return AppResult<ApplyResult>.Fail(pinError);

        proposal.TargetId ??= artifact.Id;

        // The pin's provenance rides the Artifact target — no dedicated reference type.
        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, artifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(artifact.Id, SourceReferenceTargetType.Artifact));
    }

    /// <summary>
    /// Creates or updates the pin for (attachment, artifact) after verifying the
    /// attachment really is this batch's source's stored map image. Returns null on
    /// success or the AppError to fail the apply with.
    /// </summary>
    private async Task<AppError?> CreatePlacemarkAsync(
        ReviewBatch batch, Guid artifactId, Guid attachmentId,
        decimal x, decimal y, string? label, decimal? confidence, CancellationToken ct)
    {
        var attachment = await _sourceAttachmentRepository.GetByIdAsync(attachmentId, ct);
        if (attachment is null
            || attachment.SourceId != batch.SourceId
            || attachment.Kind != SourceAttachmentKind.MapImage
            || attachment.Status != SourceAttachmentStatus.Stored)
        {
            return new AppError(400, "invalid_payload",
                "The placemark's attachment is not this source's stored map image.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _mapPlacemarkRepository.GetByAttachmentAndArtifactAsync(attachmentId, artifactId, ct);
        if (existing is not null)
        {
            // One pin per (map, artifact): re-accepts update in place.
            existing.X = x;
            existing.Y = y;
            existing.Label = label;
            existing.Confidence = confidence;
            existing.UpdatedAt = now;
            await _mapPlacemarkRepository.UpdateAsync(existing, ct);
            return null;
        }

        await _mapPlacemarkRepository.CreateAsync(new MapPlacemark
        {
            Id = Guid.NewGuid(),
            WorldId = batch.WorldId,
            SourceAttachmentId = attachmentId,
            ArtifactId = artifactId,
            X = x,
            Y = y,
            Label = label,
            Confidence = confidence,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        return null;
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

        var resolvedNow = false;
        if (payload.Status is not null && Enum.TryParse<ArtifactStatus>(payload.Status, ignoreCase: true, out var status))
        {
            artifact.Status = status;
            resolvedNow = status == ArtifactStatus.Resolved;
        }

        artifact.UpdatedAt = DateTimeOffset.UtcNow;

        await _artifactRepository.UpdateAsync(artifact, ct);

        // A storyline resolved by accepting a wrap-up/retrospective closure settles its
        // provisional facts to Confirmed, exactly as the artifact-page action does.
        if (resolvedNow && artifact.Type == ArtifactType.Storyline)
        {
            await StorylineResolution.SettleFactsAsync(_artifactFactRepository, artifact.Id, artifact.UpdatedAt, ct);
        }

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

        // Reassign map pins to the merge target; when the target already has a pin on
        // the same map (unique key), the target's pin wins and the source's is dropped.
        foreach (var placemark in await _mapPlacemarkRepository.ListByArtifactAsync(payload.SourceArtifactId, ct))
        {
            var collision = await _mapPlacemarkRepository.GetByAttachmentAndArtifactAsync(
                placemark.SourceAttachmentId, targetArtifact.Id, ct);
            if (collision is not null)
            {
                await _mapPlacemarkRepository.DeleteAsync(placemark.Id, ct);
                continue;
            }

            placemark.ArtifactId = targetArtifact.Id;
            placemark.UpdatedAt = DateTimeOffset.UtcNow;
            await _mapPlacemarkRepository.UpdateAsync(placemark, ct);
        }

        // Archive the source artifact
        sourceArtifact.Status = ArtifactStatus.Archived;
        sourceArtifact.UpdatedAt = DateTimeOffset.UtcNow;
        await _artifactRepository.UpdateAsync(sourceArtifact, ct);

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, targetArtifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(targetArtifact.Id, SourceReferenceTargetType.Artifact));
    }

    private async Task<AppResult<ApplyResult>> ApplyAddFact(
        ReviewProposal proposal, ReviewBatch batch, VisibilityFilter actingFilter, CancellationToken ct)
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
            var resolution = await ResolveArtifactByNameAsync(batch.WorldId, payload.ArtifactName, actingFilter, ct);
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
            CreatedByUserId = source.CreatedByUserId,
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
        ReviewProposal proposal, ReviewBatch batch, VisibilityFilter actingFilter, CancellationToken ct)
    {
        var payload = Deserialize<AddRelationshipPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize AddRelationship payload."));

        // Resolve both endpoints: by id, or by name for artifacts created earlier in the
        // same batch (their GUIDs did not exist at extraction time).
        var endpointA = await ResolveRelationshipEndpointAsync(
            batch.WorldId, payload.ArtifactAId, payload.ArtifactAName, "ArtifactA", actingFilter, ct);
        if (!endpointA.IsSuccess)
            return AppResult<ApplyResult>.Fail(endpointA.Error!);

        var endpointB = await ResolveRelationshipEndpointAsync(
            batch.WorldId, payload.ArtifactBId, payload.ArtifactBName, "ArtifactB", actingFilter, ct);
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

        // PartOf is structural, not additive: a storyline sits under exactly one parent. An
        // approved proposal therefore *moves* the child rather than giving it a second parent,
        // which is what silently accumulated duplicate rows and broke the parent editor.
        if (string.Equals(payload.Type, ArtifactService.PartOfRelationshipType, StringComparison.Ordinal))
        {
            var existingLinks = (await _artifactRelationshipRepository.ListByArtifactAsync(artifactA.Id, ct))
                .Where(r => r.Type == ArtifactService.PartOfRelationshipType && r.ArtifactAId == artifactA.Id)
                .ToList();

            // Surplus rows from before the invariant was enforced; the first one is rewritten.
            foreach (var surplus in existingLinks.Skip(1))
            {
                await _artifactRelationshipRepository.DeleteAsync(surplus.Id, ct);
            }

            if (existingLinks.FirstOrDefault() is { } current)
            {
                current.ArtifactBId = artifactB.Id;
                current.Description = payload.Description ?? current.Description;
                current.Confidence = payload.Confidence ?? current.Confidence;
                current.TruthState = truthState;
                current.Visibility = visibility;
                current.UpdatedAt = now;
                await _artifactRelationshipRepository.UpdateAsync(current, ct);

                await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactRelationship, current.Id, proposal.Id, ct);

                return AppResult<ApplyResult>.Success(new ApplyResult(current.Id, SourceReferenceTargetType.ArtifactRelationship));
            }
        }

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
            CreatedByUserId = source.CreatedByUserId,
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
    /// Resolves an artifact by exact name within the world, seeing only what the accepting
    /// reviewer may see. Fails when the name matches nothing (the referenced CreateArtifact
    /// proposal was rejected or not yet accepted, or the name belongs to an artifact hidden
    /// from this reviewer) or more than one artifact (ambiguous — the reviewer must edit the
    /// proposal to use an id).
    ///
    /// The reviewer's own name is not enough to make this safe: a Player may review proposals
    /// on their own sources, and the proposal payload is Player-editable, so an unfiltered
    /// lookup here would both bind their facts to artifacts they cannot see and act as a
    /// name-probe over the world's whole artifact table.
    ///
    /// It matters that <paramref name="actingFilter"/> is applied inside the query rather than
    /// to the result: the not-found / ambiguous split is then computed over the visible set
    /// alone, so it distinguishes only between states the reviewer could already establish by
    /// listing their own artifacts. That keeps both messages actionable without leaking.
    /// </summary>
    private async Task<AppResult<Artifact>> ResolveArtifactByNameAsync(
        Guid worldId, string name, VisibilityFilter actingFilter, CancellationToken ct)
    {
        var matches = await _artifactRepository.ListByExactNameAsync(worldId, name.Trim(), actingFilter, ct);

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
        Guid worldId, Guid? artifactId, string? artifactName, string endpointLabel,
        VisibilityFilter actingFilter, CancellationToken ct)
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
            return await ResolveArtifactByNameAsync(worldId, artifactName, actingFilter, ct);

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
