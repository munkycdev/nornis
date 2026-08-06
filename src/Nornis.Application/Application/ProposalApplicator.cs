using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <summary>
    /// Candidate artifacts for the CreateArtifact dedup, keyed by (world, type). Accepting a
    /// 50-proposal batch used to reload every artifact of the proposed type once per create, plus
    /// a membership lookup each time to build the author's visibility filter.
    ///
    /// This applicator is registered scoped, so the cache lives exactly as long as one request.
    /// It is NOT a general artifact cache — see <see cref="GetDedupCandidatesAsync"/> for what
    /// keeps it honest.
    /// </summary>
    private readonly Dictionary<(Guid WorldId, ArtifactType Type), List<Artifact>> _dedupCandidates = new();

    /// <summary>Author visibility filters, which cost a membership query each to build.</summary>
    private readonly Dictionary<(Guid WorldId, Guid AuthorUserId), VisibilityFilter> _authorFilters = new();

    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IMapPlacemarkRepository _mapPlacemarkRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;

    public ProposalApplicator(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IMapPlacemarkRepository mapPlacemarkRepository,
        IWorldMemberRepository worldMemberRepository)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _mapPlacemarkRepository = mapPlacemarkRepository;
        _worldMemberRepository = worldMemberRepository;
    }

    public async Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        return proposal.ChangeType switch
        {
            ReviewChangeType.CreateArtifact => await ApplyCreateArtifact(proposal, batch, source, ct),
            ReviewChangeType.UpdateArtifact => await ApplyUpdateArtifact(proposal, batch, source, actingFilter, ct),
            ReviewChangeType.MergeArtifact => await ApplyMergeArtifact(proposal, batch, source, actingFilter, ct),
            ReviewChangeType.AddFact => await ApplyAddFact(proposal, batch, source, actingFilter, ct),
            ReviewChangeType.UpdateFact => await ApplyUpdateFact(proposal, batch, source, actingFilter, ct),
            ReviewChangeType.AddRelationship => await ApplyAddRelationship(proposal, batch, source, actingFilter, ct),
            ReviewChangeType.UpdateRelationship => await ApplyUpdateRelationship(proposal, batch, source, actingFilter, ct),
            ReviewChangeType.AddPlacemark => await ApplyAddPlacemark(proposal, batch, source, actingFilter, ct),
            _ => AppResult<ApplyResult>.Fail(new AppError(400, "unknown_change_type", $"Unknown change type: {proposal.ChangeType}"))
        };
    }

    private async Task<AppResult<ApplyResult>> ApplyCreateArtifact(
        ReviewProposal proposal, ReviewBatch batch, Source source, CancellationToken ct)
    {
        var payload = Deserialize<CreateArtifactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize CreateArtifact payload."));

        if (!Enum.TryParse<ArtifactType>(payload.Type, ignoreCase: true, out var artifactType))
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_artifact_type", $"Invalid artifact type: {payload.Type}"));


        // Apply-time dedup backstop: sources readied together all extract against the same
        // canon, so several batches can each propose the same artifact. Bind to what is
        // already there rather than minting a twin the GM has to merge later.
        var match = await FindMatchingArtifactAsync(batch.WorldId, artifactType, payload.Name, source, ct);
        if (match is not null)
        {
            // The existing artifact is vetted canon: its summary, visibility and confidence
            // are NOT overwritten by this fresh extraction. Only provenance is added.
            if (payload.MapPlacemark is { } matchedPin)
            {
                var matchedPinError = await CreatePlacemarkAsync(
                    batch, match.Id, matchedPin.AttachmentId, matchedPin.X, matchedPin.Y,
                    matchedPin.Label, payload.Confidence, ct);
                if (matchedPinError is not null)
                    return AppResult<ApplyResult>.Fail(matchedPinError);
            }

            proposal.TargetId = match.Id;
            proposal.AppliedToExistingArtifact = true;

            await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, match.Id, proposal.Id, ct);

            return AppResult<ApplyResult>.Success(
                new ApplyResult(match.Id, SourceReferenceTargetType.Artifact, MatchedExistingArtifact: true));
        }

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
        proposal.AppliedToExistingArtifact = false;

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, artifact.Id, proposal.Id, ct);

        // The next create in this request must be able to dedup against this one, or two proposals
        // naming the same new artifact would both miss and both insert. Last, not right after the
        // insert: everything above can still fail the apply and roll the artifact back.
        RememberCreatedArtifact(artifact);

        return AppResult<ApplyResult>.Success(new ApplyResult(artifact.Id, SourceReferenceTargetType.Artifact)
        {
            // A birth summary is the reviewer's accepted text; born without one, the
            // refresh fills the hole from the facts accepted alongside it.
            SummaryRefreshCandidates = payload.Summary is null ? [artifact.Id] : [],
            SummaryPinnedArtifactIds = payload.Summary is null ? [] : [artifact.Id]
        });
    }

    /// <summary>
    /// The duplicate an incoming CreateArtifact should bind to, or null to insert normally.
    ///
    /// Candidates are Active artifacts of the same type in the same world whose name is
    /// equivalent under <see cref="ArtifactNameKey"/>, restricted to what the SOURCE'S side
    /// may see — never the accepting reviewer's. A GM accepting a player's source must not
    /// silently bind it to a GM-hidden artifact: that both tells the player the hidden thing
    /// exists (its name now resolves for them) and files their note as provenance for canon
    /// they were never shown. When a same-name artifact falls outside that sight we
    /// deliberately create the duplicate and leave it to manual merge.
    /// </summary>
    private async Task<Artifact?> FindMatchingArtifactAsync(
        Guid worldId, ArtifactType type, string proposedName, Source source, CancellationToken ct)
    {
        if (ArtifactNameKey.Collapse(proposedName).Length == 0)
            return null;

        var authorFilter = await GetAuthorFilterAsync(worldId, source.CreatedByUserId, ct);

        // Two gates, and a candidate must pass both.
        //
        // The author gate (above) is about the person. The source gate (here) is about the
        // note: matching attaches a SourceReference carrying this source's verbatim extraction
        // quote to the artifact, and artifact detail hands those quotes to everyone who can see
        // the artifact. Binding a Private note to a PartyVisible artifact would therefore
        // publish the note's own words to the whole world. So a match may never reach WIDER
        // than the source's own audience — which is also exactly the canon the extractor was
        // shown, so it can only ever bind to something it genuinely failed to spot.
        var sourceFilter = VisibilityFilter.ForSourceContext(source.Visibility, source.CreatedByUserId);

        var candidates = (await GetDedupCandidatesAsync(worldId, type, ct))
            .Where(a => a.Status == ArtifactStatus.Active
                && authorFilter.CanSee(a.Visibility, a.CreatedByUserId)
                && sourceFilter.CanSee(a.Visibility, a.CreatedByUserId)
                && ArtifactNameKey.AreEquivalent(a.Name, proposedName))
            // Canon may already hold duplicates. Pick the same one every time: exact-case match
            // wins, then the oldest (the original), then id as a last resort so the choice is
            // total even for rows minted in the same tick.
            .OrderByDescending(a => ArtifactNameKey.AreExactCaseEquivalent(a.Name, proposedName))
            .ThenBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToList();

        // The set is a snapshot, so the winner is confirmed against the database before it is
        // used. On a miss the next candidate gets its turn — canon can hold same-name duplicates,
        // and one of them going stale is no reason to skip the rest.
        foreach (var candidate in candidates)
        {
            if (await ConfirmMatchAsync(candidate, proposedName, authorFilter, sourceFilter, ct))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Every non-archived artifact of this type in this world, read once per (world, type) and
    /// then kept current in memory for the life of the request.
    ///
    /// <para><b>Deliberately unfiltered.</b> The visibility gates run in memory in
    /// <see cref="FindMatchingArtifactAsync"/> instead, over the same rows and by the same
    /// <see cref="VisibilityFilter.CanSee"/> the SQL predicate uses — so the outcome is identical.
    /// Reading per-author sets instead would be the narrower query but the wrong cache: one
    /// request accepts proposals from several authors' sources, and an artifact created against
    /// one author's set would leave every other author's set stale. That is the "Salt Factor"
    /// failure — two proposals naming the same new artifact both miss the dedup and both insert,
    /// stranding half the batch's facts on a duplicate. Keying on what the ROW is, rather than on
    /// who is looking, makes that unrepresentable.</para>
    ///
    /// <para>Anything that could change whether a row still belongs here — a rename, a status
    /// change, an archive on merge — drops the whole cache rather than trying to patch it.</para>
    /// </summary>
    private async Task<IReadOnlyList<Artifact>> GetDedupCandidatesAsync(
        Guid worldId, ArtifactType type, CancellationToken ct)
    {
        if (_dedupCandidates.TryGetValue((worldId, type), out var cached))
        {
            return cached;
        }

        var loaded = (await _artifactRepository.ListByTypeAsync(worldId, type, VisibilityFilter.All, ct)).ToList();
        _dedupCandidates[(worldId, type)] = loaded;
        return loaded;
    }

    /// <summary>
    /// Adds a just-created artifact to its cached candidate set, so the next create in the same
    /// request can dedup against it. Called only once the create has fully succeeded — an artifact
    /// whose apply failed after the insert is about to be rolled back and must never be offered.
    /// </summary>
    private void RememberCreatedArtifact(Artifact artifact)
    {
        if (!_dedupCandidates.TryGetValue((artifact.WorldId, artifact.Type), out var cached))
        {
            // Nothing cached for this (world, type) yet — the next lookup reads it fresh and
            // will include this artifact anyway.
            return;
        }

        if (artifact.Status == ArtifactStatus.Archived)
        {
            // Mirrors the one non-visibility clause of the SQL predicate. Visibility is not
            // tested here: the set is unfiltered by design and the gates run at match time.
            return;
        }

        cached.Add(artifact);
    }

    /// <summary>
    /// Re-reads a chosen candidate and re-runs every gate against the fresh row. Only a candidate
    /// that still holds up is allowed to become the match.
    ///
    /// <para><b>Why every match, not just the interesting ones.</b> The candidate set is a
    /// snapshot, and two different things can make an entry wrong by the time it is chosen. A
    /// create applied earlier in this request may have been rolled back — each proposal gets its
    /// own transaction and the accept loop carries on afterwards on this same applicator. And
    /// another request may have renamed, archived or narrowed the visibility of an artifact this
    /// one read minutes ago. The code that this replaced re-listed before every create, so it saw
    /// both; re-reading only the ids created here would fix the first and quietly keep the second.
    /// One rule that covers both is easier to keep true than two with a carve-out.</para>
    ///
    /// <para>Getting it wrong is not a stale read, it is a wrong write: the proposal is reported
    /// as "bound to existing", provenance commits against an artifact that is gone, renamed, or
    /// hidden from the source's own audience, and every fact in the batch that named it then
    /// fails to resolve with the create already Accepted and unable to be reopened.</para>
    ///
    /// <para>The cost is one primary-key lookup per successful dedup, against the full listing
    /// per create that this change removed. It closes the window to the same width the old code
    /// had — read then write, with no lock in between — rather than to zero.</para>
    /// </summary>
    private async Task<bool> ConfirmMatchAsync(
        Artifact candidate, string proposedName,
        VisibilityFilter authorFilter, VisibilityFilter sourceFilter, CancellationToken ct)
    {
        var fresh = await _artifactRepository.GetByIdAsync(candidate.Id, ct);

        if (fresh is not null
            && fresh.Status == ArtifactStatus.Active
            && authorFilter.CanSee(fresh.Visibility, fresh.CreatedByUserId)
            && sourceFilter.CanSee(fresh.Visibility, fresh.CreatedByUserId)
            && ArtifactNameKey.AreEquivalent(fresh.Name, proposedName))
        {
            return true;
        }

        // Stale. Replace the snapshot's copy with what the database actually holds so the next
        // proposal in this request is judged on the fresh row — or drop it entirely if it is gone
        // or archived, which is the one state the set never holds.
        foreach (var set in _dedupCandidates.Values)
        {
            set.RemoveAll(a => a.Id == candidate.Id);
        }

        if (fresh is not null
            && fresh.Status != ArtifactStatus.Archived
            && _dedupCandidates.TryGetValue((fresh.WorldId, fresh.Type), out var cached))
        {
            cached.Add(fresh);
        }

        return false;
    }

    /// <summary>
    /// Drops every cached candidate set. Called by the arms that rename, re-status or archive an
    /// artifact: each of those can change whether a row still matches, and patching the cache per
    /// mutation is exactly the kind of cleverness that reintroduces a duplicate-creation bug.
    /// These arms are rare within a request, so refetching costs little.
    /// </summary>
    private void InvalidateDedupCandidates() => _dedupCandidates.Clear();

    private async Task<VisibilityFilter> GetAuthorFilterAsync(Guid worldId, Guid authorUserId, CancellationToken ct)
    {
        if (_authorFilters.TryGetValue((worldId, authorUserId), out var cached))
        {
            return cached;
        }

        var filter = await ResolveAuthorFilterAsync(worldId, authorUserId, ct);
        _authorFilters[(worldId, authorUserId)] = filter;
        return filter;
    }

    /// <summary>
    /// What the source's author may see in this world. A source whose author has since lost
    /// their membership falls back to Player-level sight for that user id, which is the
    /// narrowest filter that still lets them match their own Private artifacts.
    /// </summary>
    private async Task<VisibilityFilter> ResolveAuthorFilterAsync(
        Guid worldId, Guid authorUserId, CancellationToken ct)
    {
        var member = await _worldMemberRepository.GetByWorldAndUserAsync(worldId, authorUserId, ct);
        return VisibilityFilter.ForRole(member?.Role ?? WorldRole.Player, authorUserId);
    }

    private async Task<AppResult<ApplyResult>> ApplyAddPlacemark(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        var payload = Deserialize<AddPlacemarkPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize AddPlacemark payload."));

        // Resolve the artifact: TargetId, payload id, or name (ambiguity surfaces to the
        // reviewer exactly like name-referenced facts).
        var artifactId = proposal.TargetId ?? payload.ArtifactId;
        if ((artifactId is null || artifactId == Guid.Empty) && string.IsNullOrWhiteSpace(payload.ArtifactName))
        {
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload",
                "AddPlacemark requires an ArtifactId or ArtifactName."));
        }

        var artifactResolution = await ResolveTargetArtifactAsync(
            batch.WorldId, artifactId, payload.ArtifactName, actingFilter, ct);
        if (!artifactResolution.IsSuccess)
            return AppResult<ApplyResult>.Fail(artifactResolution.Error!);
        var artifact = artifactResolution.Value!;

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
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "UpdateArtifact requires a TargetId."));

        var payload = Deserialize<UpdateArtifactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize UpdateArtifact payload."));

        var resolution = await ResolveTargetArtifactAsync(batch.WorldId, proposal.TargetId, null, actingFilter, ct);
        if (!resolution.IsSuccess)
            return AppResult<ApplyResult>.Fail(resolution.Error!);
        var artifact = resolution.Value!;


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

        // Name, visibility and status are all inputs to the dedup query, and this arm can change
        // any of them.
        InvalidateDedupCandidates();

        // A storyline resolved by accepting a wrap-up/retrospective closure settles its
        // provisional facts to Confirmed, exactly as the artifact-page action does.
        if (resolvedNow && artifact.Type == ArtifactType.Storyline)
        {
            await StorylineResolution.SettleFactsAsync(_artifactFactRepository, artifact.Id, artifact.UpdatedAt, ct);
        }

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, artifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(artifact.Id, SourceReferenceTargetType.Artifact)
        {
            // Name and status shape what a summary says; visibility and confidence shape
            // who sees it, and report nothing.
            SummaryRefreshCandidates =
                payload.Summary is null && (payload.Name is not null || payload.Status is not null)
                    ? [artifact.Id]
                    : [],
            SummaryPinnedArtifactIds = payload.Summary is null ? [] : [artifact.Id]
        });
    }

    private async Task<AppResult<ApplyResult>> ApplyMergeArtifact(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "MergeArtifact requires a TargetId."));

        var payload = Deserialize<MergeArtifactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize MergeArtifact payload."));

        var targetResolution = await ResolveTargetArtifactAsync(batch.WorldId, proposal.TargetId, null, actingFilter, ct);
        if (!targetResolution.IsSuccess)
            return AppResult<ApplyResult>.Fail(targetResolution.Error!);
        var targetArtifact = targetResolution.Value!;

        var sourceResolution = await ResolveTargetArtifactAsync(batch.WorldId, payload.SourceArtifactId, null, actingFilter, ct);
        if (!sourceResolution.IsSuccess)
            return AppResult<ApplyResult>.Fail(new AppError(404, "source_artifact_not_found", "Source artifact for merge not found."));
        var sourceArtifact = sourceResolution.Value!;


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

        // Reassign facts from source artifact to target artifact — one save for the
        // collection, not one per row.
        var sourceFacts = await _artifactFactRepository.ListByArtifactAsync(payload.SourceArtifactId, ct);
        foreach (var fact in sourceFacts)
        {
            fact.ArtifactId = targetArtifact.Id;
        }
        await _artifactFactRepository.UpdateRangeAsync(sourceFacts, ct);

        // Reassign relationships from source artifact to target artifact.
        var sourceRelationships = await _artifactRelationshipRepository.ListByArtifactAsync(payload.SourceArtifactId, ct);
        var reassignedRelationships = new List<ArtifactRelationship>();
        foreach (var relationship in sourceRelationships)
        {
            // Decide the self-reference case BEFORE touching the entity: these rows may
            // be tracked, and a mutated-but-skipped entity would still flush with the
            // next SaveChanges.
            var newA = relationship.ArtifactAId == payload.SourceArtifactId ? targetArtifact.Id : relationship.ArtifactAId;
            var newB = relationship.ArtifactBId == payload.SourceArtifactId ? targetArtifact.Id : relationship.ArtifactBId;
            if (newA == newB)
            {
                // The duplicate was related to the target it is being merged into, so the
                // row would now join the target to itself. Delete it rather than leave it:
                // skipping only avoids reassignment, and the untouched row still points at
                // the artifact about to be archived. Left alive it is a permanent tax —
                // the target's detail page lists the archived duplicate as a connection,
                // and every continuity audit re-raises it as "Unknown artifact" evidence
                // that no cleanup can reach.
                await _artifactRelationshipRepository.DeleteAsync(relationship.Id, ct);
                continue;
            }

            relationship.ArtifactAId = newA;
            relationship.ArtifactBId = newB;
            reassignedRelationships.Add(relationship);
        }
        await _artifactRelationshipRepository.UpdateRangeAsync(reassignedRelationships, ct);

        // Reassign map pins to the merge target; when the target already has a pin on
        // the same map (unique key), the target's pin wins and the source's is dropped.
        var reassignedPlacemarks = new List<MapPlacemark>();
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
            reassignedPlacemarks.Add(placemark);
        }
        await _mapPlacemarkRepository.UpdateRangeAsync(reassignedPlacemarks, ct);

        // Archive the source artifact
        sourceArtifact.Status = ArtifactStatus.Archived;
        sourceArtifact.UpdatedAt = DateTimeOffset.UtcNow;
        await _artifactRepository.UpdateAsync(sourceArtifact, ct);

        // The archived source must stop matching, and the merge target's name or visibility may
        // have moved too.
        InvalidateDedupCandidates();

        await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.Artifact, targetArtifact.Id, proposal.Id, ct);

        return AppResult<ApplyResult>.Success(new ApplyResult(targetArtifact.Id, SourceReferenceTargetType.Artifact)
        {
            // The target absorbed the duplicate's facts and relationships whether or not
            // the payload touched a field; the archived duplicate needs no summary again.
            SummaryRefreshCandidates = payload.Summary is null ? [targetArtifact.Id] : [],
            SummaryPinnedArtifactIds = payload.Summary is null ? [] : [targetArtifact.Id]
        });
    }

    private async Task<AppResult<ApplyResult>> ApplyAddFact(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        var payload = Deserialize<AddFactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize AddFact payload."));

        // Resolve the target artifact: by TargetId, or by name for artifacts created earlier
        // in the same batch (their GUIDs did not exist at extraction time).
        if (proposal.TargetId is null && string.IsNullOrWhiteSpace(payload.ArtifactName))
        {
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id",
                "AddFact requires a TargetId or an artifactName referencing an Artifact."));
        }

        var artifactResolution = await ResolveTargetArtifactAsync(
            batch.WorldId, proposal.TargetId, payload.ArtifactName, actingFilter, ct);
        if (!artifactResolution.IsSuccess)
            return AppResult<ApplyResult>.Fail(artifactResolution.Error!);
        var artifact = artifactResolution.Value!;


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

        return AppResult<ApplyResult>.Success(new ApplyResult(fact.Id, SourceReferenceTargetType.ArtifactFact)
        {
            SummaryRefreshCandidates = [artifact.Id]
        });
    }

    private async Task<AppResult<ApplyResult>> ApplyUpdateFact(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "UpdateFact requires a TargetId."));

        var payload = Deserialize<UpdateFactPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize UpdateFact payload."));

        var fact = await _artifactFactRepository.GetByIdAsync(proposal.TargetId.Value, ct);
        if (fact is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target fact not found."));

        // The fact is scoped through its parent artifact AND its own visibility: wrong
        // world, a hidden parent, or a fact the accepter may not see itself (a GM-only
        // or other-user Private row under a shared artifact) all read as the same 404.
        var parent = await ResolveTargetArtifactAsync(batch.WorldId, fact.ArtifactId, null, actingFilter, ct);
        if (!parent.IsSuccess || !actingFilter.CanSee(fact.Visibility, fact.CreatedByUserId))
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target fact not found."));


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

        return AppResult<ApplyResult>.Success(new ApplyResult(fact.Id, SourceReferenceTargetType.ArtifactFact)
        {
            SummaryRefreshCandidates = payload.Value is not null || payload.TruthState is not null
                ? [fact.ArtifactId]
                : []
        });
    }

    private async Task<AppResult<ApplyResult>> ApplyAddRelationship(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
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


        var now = DateTimeOffset.UtcNow;
        var visibility = ResolveVisibility(payload.Visibility, source);
        var truthState = ResolveTruthState(payload.TruthState);

        // PartOf is structural, not additive: a storyline sits under exactly one parent. An
        // approved proposal therefore *moves* the child rather than giving it a second parent,
        // which is what silently accumulated duplicate rows and broke the parent editor.
        // Both the PartOf branch and the duplicate-edge check below need this same list, and the
        // PartOf branch falls through to it when the storyline has no parent yet — so fetching
        // once covers both. Every accepted storyline-hierarchy edge used to pay for it twice.
        var relationshipsForA = await _artifactRelationshipRepository.ListByArtifactAsync(artifactA.Id, ct);

        if (string.Equals(payload.Type, ArtifactService.PartOfRelationshipType, StringComparison.Ordinal))
        {
            var existingLinks = relationshipsForA
                .Where(r => r.Type == ArtifactService.PartOfRelationshipType && r.ArtifactAId == artifactA.Id)
                .ToList();

            // Surplus rows from before the invariant was enforced; the first one is rewritten.
            foreach (var surplus in existingLinks.Skip(1))
            {
                await _artifactRelationshipRepository.DeleteAsync(surplus.Id, ct);
            }

            if (existingLinks.FirstOrDefault() is { } current)
            {
                var previousParentId = current.ArtifactBId;
                current.ArtifactBId = artifactB.Id;
                current.Description = payload.Description ?? current.Description;
                current.Confidence = payload.Confidence ?? current.Confidence;
                current.TruthState = truthState;
                current.Visibility = visibility;
                current.UpdatedAt = now;
                await _artifactRelationshipRepository.UpdateAsync(current, ct);

                await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactRelationship, current.Id, proposal.Id, ct);

                return AppResult<ApplyResult>.Success(new ApplyResult(current.Id, SourceReferenceTargetType.ArtifactRelationship)
                {
                    // A PartOf move stales three summaries: the child, the new parent, and
                    // the parent it just left.
                    SummaryRefreshCandidates = previousParentId == artifactB.Id
                        ? [artifactA.Id, artifactB.Id]
                        : [artifactA.Id, artifactB.Id, previousParentId]
                });
            }
        }

        // Re-proposing an edge that already exists (same direction, same type) reinforces
        // it instead of duplicating the row: the new source cites the existing relationship.
        // Sessions routinely re-state established connections, and each acceptance used to
        // add another identical row.
        var duplicate = relationshipsForA
            .FirstOrDefault(r => r.ArtifactAId == artifactA.Id
                && r.ArtifactBId == artifactB.Id
                && string.Equals(r.Type, payload.Type, StringComparison.Ordinal));
        if (duplicate is not null)
        {
            duplicate.Description = payload.Description ?? duplicate.Description;
            duplicate.Confidence = payload.Confidence ?? duplicate.Confidence;
            duplicate.TruthState = truthState;
            duplicate.Visibility = visibility;
            duplicate.UpdatedAt = now;
            await _artifactRelationshipRepository.UpdateAsync(duplicate, ct);

            await CreateSourceReference(batch.SourceId, SourceReferenceTargetType.ArtifactRelationship, duplicate.Id, proposal.Id, ct);

            return AppResult<ApplyResult>.Success(new ApplyResult(duplicate.Id, SourceReferenceTargetType.ArtifactRelationship)
            {
                SummaryRefreshCandidates = [artifactA.Id, artifactB.Id]
            });
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

        return AppResult<ApplyResult>.Success(new ApplyResult(relationship.Id, SourceReferenceTargetType.ArtifactRelationship)
        {
            SummaryRefreshCandidates = [artifactA.Id, artifactB.Id]
        });
    }

    private async Task<AppResult<ApplyResult>> ApplyUpdateRelationship(
        ReviewProposal proposal, ReviewBatch batch, Source source, VisibilityFilter actingFilter, CancellationToken ct)
    {
        if (proposal.TargetId is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "missing_target_id", "UpdateRelationship requires a TargetId."));

        var payload = Deserialize<UpdateRelationshipPayload>(proposal.ProposedValueJson);
        if (payload is null)
            return AppResult<ApplyResult>.Fail(new AppError(400, "invalid_payload", "Failed to deserialize UpdateRelationship payload."));

        var relationship = await _artifactRelationshipRepository.GetByIdAsync(proposal.TargetId.Value, ct);
        if (relationship is null)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target relationship not found."));

        // The relationship is scoped through its endpoint artifacts AND its own
        // visibility: either endpoint wrong-world or hidden, or the relationship row
        // itself invisible to the accepter, reads as the same 404.
        if (!actingFilter.CanSee(relationship.Visibility, relationship.CreatedByUserId))
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target relationship not found."));
        var endpointACheck = await ResolveTargetArtifactAsync(batch.WorldId, relationship.ArtifactAId, null, actingFilter, ct);
        var endpointBCheck = endpointACheck.IsSuccess
            ? await ResolveTargetArtifactAsync(batch.WorldId, relationship.ArtifactBId, null, actingFilter, ct)
            : endpointACheck;
        if (!endpointACheck.IsSuccess || !endpointBCheck.IsSuccess)
            return AppResult<ApplyResult>.Fail(new AppError(404, "target_not_found", "Target relationship not found."));


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

        return AppResult<ApplyResult>.Success(new ApplyResult(relationship.Id, SourceReferenceTargetType.ArtifactRelationship)
        {
            SummaryRefreshCandidates =
                payload.Type is not null || payload.Description is not null || payload.TruthState is not null
                    ? [relationship.ArtifactAId, relationship.ArtifactBId]
                    : []
        });
    }

    /// <summary>
    /// Resolves an artifact by name within the world, seeing only what the accepting reviewer
    /// may see. Fails when the name matches nothing (the referenced CreateArtifact proposal was
    /// rejected or not yet accepted, or the name belongs to an artifact hidden from this
    /// reviewer) or more than one artifact (ambiguous — the reviewer must edit the proposal to
    /// use an id).
    ///
    /// "Matches" means <see cref="ArtifactNameKey"/> equivalence, the same policy apply-time
    /// dedup binds on. The two must agree: while resolution was whitespace-exact and dedup was
    /// not, a create proposing "Salt  Factor" would silently bind to canon's "Salt Factor" and
    /// then every fact in the batch referencing "Salt  Factor" failed with
    /// artifact_name_not_found — unrecoverably, since the create was already Accepted so
    /// neither the retry pass nor the prerequisite cascade could do anything about it.
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
        var matches = await _artifactRepository.ListByEquivalentNameAsync(worldId, name, actingFilter, ct);

        return matches.Count switch
        {
            0 => AppResult<Artifact>.Fail(new AppError(404, "artifact_name_not_found",
                $"No artifact named '{name}' exists in this world. If it is proposed in this batch, accept its Create proposal first.")),
            1 => AppResult<Artifact>.Success(matches[0]),
            _ => AppResult<Artifact>.Fail(new AppError(409, "artifact_name_ambiguous",
                $"Multiple artifacts are named '{name}'. Edit the proposal to reference the intended artifact by id."))
        };
    }

    /// <summary>
    /// The one authorized artifact resolution for proposal targets: by id, or by name for
    /// artifacts created earlier in the same batch. The by-id path enforces world scope
    /// AND the accepter's visibility, exactly as the by-name path always has — the
    /// payload is Player-editable, so an id can never be trusted to point inside the
    /// world or at something the accepter may see. Failures read as a plain 404: the
    /// caller learns nothing about artifacts hidden from them.
    /// </summary>
    private async Task<AppResult<Artifact>> ResolveTargetArtifactAsync(
        Guid worldId, Guid? artifactId, string? artifactName,
        VisibilityFilter actingFilter, CancellationToken ct)
    {
        if (artifactId is not null && artifactId != Guid.Empty)
        {
            var artifact = await _artifactRepository.GetByIdAsync(artifactId.Value, ct);
            if (artifact is null
                || artifact.WorldId != worldId
                || !actingFilter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
            {
                return AppResult<Artifact>.Fail(new AppError(404, "target_not_found", "Target artifact not found."));
            }

            return AppResult<Artifact>.Success(artifact);
        }

        if (!string.IsNullOrWhiteSpace(artifactName))
            return await ResolveArtifactByNameAsync(worldId, artifactName, actingFilter, ct);

        return AppResult<Artifact>.Fail(new AppError(400, "missing_target_id",
            "A TargetId or artifact name is required."));
    }

    private async Task<AppResult<Artifact>> ResolveRelationshipEndpointAsync(
        Guid worldId, Guid? artifactId, string? artifactName, string endpointLabel,
        VisibilityFilter actingFilter, CancellationToken ct)
    {
        if (artifactId is not null && artifactId != Guid.Empty)
        {
            var resolved = await ResolveTargetArtifactAsync(worldId, artifactId, null, actingFilter, ct);
            if (!resolved.IsSuccess)
            {
                var code = endpointLabel == "ArtifactA" ? "artifact_a_not_found" : "artifact_b_not_found";
                return AppResult<Artifact>.Fail(new AppError(404, code, $"{endpointLabel} not found."));
            }

            return resolved;
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
            return JsonSerializer.Deserialize<T>(json, ProposalJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
