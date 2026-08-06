using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public interface IWorldDigestService
{
    /// <summary>The digest rendered for the caller: GMs get their digest plus the party
    /// preview; everyone else gets the party recap alone.</summary>
    Task<AppResult<WorldDigestView>> GetAsync(Guid worldId, WorldRole actingRole, CancellationToken ct);

    /// <summary>GM-invoked regeneration of both renderings.</summary>
    Task<AppResult<WorldDigestView>> GenerateAsync(
        Guid worldId, Guid actingUserId, WorldRole actingRole, CancellationToken ct);
}

/// <summary>Mirrors the audit's HasData=false convention for a world with no digest yet.</summary>
public sealed record WorldDigestView(
    bool HasData,
    DateTimeOffset? GeneratedAt,
    string? Content,
    string? PartyPreview);

/// <summary>
/// W3 — the gist's index.md: one maintained world-level synthesis (active storylines and
/// their momentum, recent movements, open questions), persisted as a read-model and shown
/// with its age rather than regenerated per visit.
///
/// The two renderings are two separately-scoped generation passes, not one — a reversal of
/// the plan's "one generation pass", recorded there. The visibility law says nothing
/// derived from GM-only material may surface at a wider scope, and a party recap produced
/// by a pass whose context held GM material is derived from it; asking the model to
/// withhold is an instruction, scoping the context is a guarantee. The party pass reads
/// the Observer-floor record — PartyVisible only, nobody's Private notes, no Hidden truth
/// states, relationships only between mutually visible artifacts — because the recap
/// renders to every member. Both passes share the audit's record formatter and caps.
/// </summary>
public class WorldDigestService : IWorldDigestService
{
    private readonly IWorldDigestRepository _digestRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IWorldDigestAiClient _aiClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly LoremasterOptions _options;

    public WorldDigestService(
        IWorldDigestRepository digestRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository,
        IWorldDigestAiClient aiClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        IOptions<LoremasterOptions> options)
    {
        _digestRepository = digestRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
        _aiClient = aiClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _options = options.Value;
    }

    /// <summary>What the party rendering says when the party-visible record is empty. Fixed
    /// text, no AI pass: a generation over nothing could only invent, and inventing is the
    /// one thing a recap grounded in canon must never do.</summary>
    public const string EmptyPartyRecap =
        "Nothing has been revealed to the party yet. The recap will fill in as the world's record grows.";

    public async Task<AppResult<WorldDigestView>> GetAsync(
        Guid worldId, WorldRole actingRole, CancellationToken ct)
    {
        var digest = await _digestRepository.GetByWorldAsync(worldId, ct);
        if (digest is null)
        {
            return AppResult<WorldDigestView>.Success(new WorldDigestView(false, null, null, null));
        }

        return AppResult<WorldDigestView>.Success(ToView(digest, actingRole));
    }

    public async Task<AppResult<WorldDigestView>> GenerateAsync(
        Guid worldId, Guid actingUserId, WorldRole actingRole, CancellationToken ct)
    {
        if (actingRole != WorldRole.GM)
        {
            return AppResult<WorldDigestView>.Fail(
                new AppError(403, "insufficient_role", "Only GMs can generate the world digest."));
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            return AppResult<WorldDigestView>.Fail(budgetError);
        }

        // GM pass: the audit's own read — everything except archived artifacts and
        // retired (False) rows. Hidden truths stay in: this rendering is the GM's alone.
        var gmRecord = await AssembleRecordAsync(worldId, VisibilityFilter.All, includeHiddenTruths: true, ct);
        if (gmRecord.ArtifactCount == 0)
        {
            return AppResult<WorldDigestView>.Fail(new AppError(400, "empty_world",
                "There is nothing to digest yet — the record has no artifacts."));
        }

        var gmMarkdown = await GenerateRenderingAsync(worldId, actingUserId, GmSystemPrompt, gmRecord.Text, ct);
        if (!gmMarkdown.IsSuccess)
        {
            return AppResult<WorldDigestView>.Fail(gmMarkdown.Error!);
        }

        // Party pass: the Observer-floor record, because the recap renders to every member.
        var partyRecord = await AssembleRecordAsync(
            worldId, VisibilityFilter.ForRole(WorldRole.Observer, actingUserId), includeHiddenTruths: false, ct);

        string partyMarkdown;
        if (partyRecord.ArtifactCount == 0)
        {
            partyMarkdown = EmptyPartyRecap;
        }
        else
        {
            var generated = await GenerateRenderingAsync(worldId, actingUserId, PartySystemPrompt, partyRecord.Text, ct);
            if (!generated.IsSuccess)
            {
                return AppResult<WorldDigestView>.Fail(generated.Error!);
            }

            partyMarkdown = generated.Value!;
        }

        var digest = new WorldDigest
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            GmContentMarkdown = gmMarkdown.Value!,
            PartyContentMarkdown = partyMarkdown,
            Model = _options.AiModel,
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratedByUserId = actingUserId
        };

        await _digestRepository.UpsertAsync(digest, ct);

        return AppResult<WorldDigestView>.Success(ToView(digest, WorldRole.GM));
    }

    private static WorldDigestView ToView(WorldDigest digest, WorldRole actingRole) =>
        actingRole == WorldRole.GM
            ? new WorldDigestView(true, digest.GeneratedAt, digest.GmContentMarkdown, digest.PartyContentMarkdown)
            : new WorldDigestView(true, digest.GeneratedAt, digest.PartyContentMarkdown, null);

    private sealed record AssembledRecord(string Text, int ArtifactCount);

    /// <summary>
    /// One audience's view of the record, formatted through the audit's own formatter and
    /// caps so the two features cannot drift apart on what "the record" means. The filter
    /// does the authorization work; the endpoint check does the rest — a relationship row
    /// can be party-visible while one of its ends is not, and naming that end would leak it.
    /// </summary>
    private async Task<AssembledRecord> AssembleRecordAsync(
        Guid worldId, VisibilityFilter filter, bool includeHiddenTruths, CancellationToken ct)
    {
        var artifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, ct))
            .Where(a => a.Status != ArtifactStatus.Archived)
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .ToList();

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

        var sources = (await _sourceRepository.ListByWorldAsync(worldId, ct))
            .Where(s => filter.CanSee(s.Visibility, s.CreatedByUserId))
            .ToList();

        // Quotes ride on their source's visibility: a reference row whose source fell out
        // of this audience's view carries a quote that audience must not read.
        var visibleSourceIds = sources.Select(s => s.Id).ToHashSet();
        references = references.Where(r => visibleSourceIds.Contains(r.SourceId)).ToList();

        var text = ContinuityAuditService.FormatWorldRecord(artifacts, facts, relationships, references, sources);
        return new AssembledRecord(text, artifacts.Count);
    }

    /// <summary>One rendering: one AI call, metered on both paths, audit-shaped errors.</summary>
    private async Task<AppResult<string>> GenerateRenderingAsync(
        Guid worldId, Guid actingUserId, string systemPrompt, string recordText, CancellationToken ct)
    {
        var request = new AiPromptRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage = recordText,
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

        WorldDigestAiResponse response;
        try
        {
            response = await _aiClient.GenerateAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await TrackUsageAsync(worldId, actingUserId, null, false, "ServiceError", ct);
            return AppResult<string>.Fail(new AppError(503, "service_unavailable",
                "The digest could not be generated right now. Try again shortly."));
        }

        await TrackUsageAsync(worldId, actingUserId, response.Usage, true, null, ct);
        return AppResult<string>.Success(response.DigestMarkdown.Trim());
    }

    private Task TrackUsageAsync(
        Guid worldId, Guid userId, AiUsage? usage, bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, userId, AiOperationType.WorldDigest, usage,
            succeeded, errorCode, fallbackModel: _options.AiModel, ct: ct);

    internal const string GmSystemPrompt =
        """
        You maintain the GM's index page for a tabletop RPG world. From the world record in
        the user message, write the GM digest: the state of the whole world at a glance.

        Structure, in markdown:
        ## Active storylines — one line each: the storyline, its momentum (advancing,
        stalled, or quiet — judge from how recently its record moved), and what most
        recently moved it. Lead with the arcs that are moving.
        ## Recent movements — the handful of changes that most alter the world's state.
        ## Open questions — the unresolved tensions the record carries, sharpest first.

        Rules:
        - Ground every line in the record; invent nothing, and bring in nothing from
          outside it. This page is for the GM alone, so hidden and GM-only material
          belongs here — flag a secret the players are close to touching.
        - 300 to 500 words. No preamble, no headings beyond the three above.
        - Weigh truth states: state Confirmed plainly, hedge Likely, attribute Rumor,
          name genuine Disputes instead of resolving them.

        Respond with a JSON object matching the structured output schema: {"digest": "..."}.
        """;

    internal const string PartySystemPrompt =
        """
        You maintain the players' recap page for a tabletop RPG world — "the story so far."
        From the world record in the user message, write the recap the party would read
        before a session.

        Structure, in markdown:
        ## The story so far — a compact narrative of what the party knows has happened.
        ## Where things stand — the current state of the arcs and places that matter.
        ## Open questions — what the party knows it does not know.

        Rules:
        - Ground every line in the record; invent nothing. The record you are given is
          exactly what the party knows: never speculate about what else might exist, and
          never mention that anything is withheld or hidden.
        - 250 to 400 words, in-world register, no meta-commentary about records or facts.
        - Weigh truth states: state Confirmed plainly, hedge Likely, attribute Rumor
          ("rumor has it…"), and present Disputes as open disagreements.

        Respond with a JSON object matching the structured output schema: {"digest": "..."}.
        """;
}
