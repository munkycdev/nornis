using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public interface ICampaignRecapService
{
    /// <summary>GM-invoked regeneration of both renderings of a campaign's "story so far".</summary>
    Task<AppResult<CampaignRecapView>> GenerateAsync(
        Guid campaignId, Guid worldId, Guid actingUserId, WorldRole actingRole, CancellationToken ct);
}

/// <summary>
/// The generated half of a campaign page: "the story so far", written from the campaign's
/// own record rather than the world's. The GM's written intro lives on
/// <c>Campaign.Description</c> and is never touched here — the two halves answer different
/// questions ("what is this campaign" versus "what has happened in it") and only one of
/// them goes stale.
///
/// Scope is what makes it a campaign recap: the artifacts are those the campaign's own
/// sources evidence, through <see cref="ICampaignRepository.GetRollupAsync"/>, so a
/// long-running world's other eras cannot bleed in.
///
/// Two renderings, two separately-scoped passes, for the reason <c>WorldDigestService</c>
/// records: nothing derived from GM-only material may surface at a wider scope, and a party
/// recap produced by a pass whose context held GM material <em>is</em> derived from it.
/// Asking the model to withhold is an instruction; scoping the context is a guarantee. This
/// matters more here than for the world digest, because the campaign page is one a player
/// opens directly.
/// </summary>
public class CampaignRecapService : ICampaignRecapService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly ICampaignRecapRepository _recapRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IRecordAssembler _recordAssembler;
    private readonly IDigestAiClient _aiClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly LoremasterOptions _options;

    public CampaignRecapService(
        ICampaignRepository campaignRepository,
        ICampaignRecapRepository recapRepository,
        IArtifactRepository artifactRepository,
        ISourceRepository sourceRepository,
        IRecordAssembler recordAssembler,
        IDigestAiClient aiClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        IOptions<LoremasterOptions> options)
    {
        _campaignRepository = campaignRepository;
        _recapRepository = recapRepository;
        _artifactRepository = artifactRepository;
        _sourceRepository = sourceRepository;
        _recordAssembler = recordAssembler;
        _aiClient = aiClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _options = options.Value;
    }

    /// <summary>
    /// How many of the campaign's artifacts reach the prompt, most-cited first. Higher than
    /// the page's list because the model reads for narrative and a bit-player can carry the
    /// turn a session took; still bounded, because the prompt is not.
    /// </summary>
    public const int MaxRecapArtifacts = 250;

    /// <summary>What the party rendering says when the party-visible record of this campaign
    /// is empty. Fixed text, no AI pass: a generation over nothing could only invent.</summary>
    public const string EmptyPartyRecap =
        "Nothing from this campaign has been revealed to the party yet. The recap will fill in as its record grows.";

    public async Task<AppResult<CampaignRecapView>> GenerateAsync(
        Guid campaignId, Guid worldId, Guid actingUserId, WorldRole actingRole, CancellationToken ct)
    {
        if (actingRole != WorldRole.GM)
        {
            return AppResult<CampaignRecapView>.Fail(
                new AppError(403, "insufficient_role", "Only GMs can generate a campaign recap."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);
        if (campaign is null || campaign.WorldId != worldId)
        {
            return AppResult<CampaignRecapView>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            return AppResult<CampaignRecapView>.Fail(budgetError);
        }

        // GM pass: the campaign's whole record, hidden truths included — the GM's alone.
        var gmRecord = await AssembleRecordAsync(campaign, VisibilityFilter.All, includeHiddenTruths: true, ct);
        if (gmRecord.ArtifactCount == 0)
        {
            return AppResult<CampaignRecapView>.Fail(new AppError(400, "empty_campaign",
                "There is nothing to recap yet — no source in this campaign has produced knowledge."));
        }

        var gmMarkdown = await GenerateRenderingAsync(
            worldId, actingUserId, GmSystemPrompt, Preamble(campaign) + gmRecord.Text, ct);
        if (!gmMarkdown.IsSuccess)
        {
            return AppResult<CampaignRecapView>.Fail(gmMarkdown.Error!);
        }

        // Party pass: the Observer-floor record, because the campaign page renders to every member.
        var partyRecord = await AssembleRecordAsync(
            campaign, VisibilityFilter.ForRole(WorldRole.Observer, actingUserId), includeHiddenTruths: false, ct);

        string partyMarkdown;
        if (partyRecord.ArtifactCount == 0)
        {
            partyMarkdown = EmptyPartyRecap;
        }
        else
        {
            var generated = await GenerateRenderingAsync(
                worldId, actingUserId, PartySystemPrompt, Preamble(campaign) + partyRecord.Text, ct);
            if (!generated.IsSuccess)
            {
                return AppResult<CampaignRecapView>.Fail(generated.Error!);
            }

            partyMarkdown = generated.Value!;
        }

        var recap = new CampaignRecap
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            GmContentMarkdown = gmMarkdown.Value!,
            PartyContentMarkdown = partyMarkdown,
            Model = _options.AiModel,
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratedByUserId = actingUserId
        };

        await _recapRepository.UpsertAsync(recap, ct);

        return AppResult<CampaignRecapView>.Success(CampaignRecapView.From(recap, WorldRole.GM));
    }

    /// <summary>
    /// Names the campaign for the model. The record text is a bare list of artifacts and
    /// sessions; without this the pass has no way to know whose story it is writing, and a
    /// recap that never names the campaign reads like the world digest.
    /// </summary>
    private static string Preamble(Campaign campaign)
    {
        var span = (campaign.StartedAt, campaign.EndedAt) switch
        {
            ({ } start, { } end) => $" Played {start:yyyy-MM-dd} to {end:yyyy-MM-dd}.",
            ({ } start, null) => $" Began {start:yyyy-MM-dd}.",
            _ => string.Empty
        };

        return $"""
            The record below is one campaign within a larger world: "{campaign.Name}" ({campaign.Status}).{span}
            Write only about this campaign.


            """;
    }

    /// <summary>
    /// The campaign's slice of the record at one audience's visibility: the artifacts its own
    /// sources evidence, and those sources. The rollup has already applied
    /// <paramref name="filter"/> in SQL, so the ids it returns are ones this audience may see —
    /// which is what makes the unfiltered <c>ListByIdsAsync</c> safe here.
    /// </summary>
    private async Task<AssembledRecord> AssembleRecordAsync(
        Campaign campaign, VisibilityFilter filter, bool includeHiddenTruths, CancellationToken ct)
    {
        var rollup = await _campaignRepository.GetRollupAsync(
            campaign.WorldId, campaign.Id, filter, MaxRecapArtifacts, ct);

        var artifacts = (await _artifactRepository.ListByIdsAsync(
                rollup.Artifacts.Select(a => a.ArtifactId).ToList(), ct))
            .Where(a => a.Status != ArtifactStatus.Archived)
            .ToList();

        var sources = (await _sourceRepository.ListByWorldAsync(campaign.WorldId, ct))
            .Where(s => s.CampaignId == campaign.Id)
            .Where(s => filter.CanSee(s.Visibility, s.CreatedByUserId))
            .ToList();

        return await _recordAssembler.AssembleAsync(artifacts, sources, filter, includeHiddenTruths, ct);
    }

    /// <summary>One rendering: one AI call, metered on both paths.</summary>
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

        DigestAiResponse response;
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
                "The recap could not be generated right now. Try again shortly."));
        }

        await TrackUsageAsync(worldId, actingUserId, response.Usage, true, null, ct);
        return AppResult<string>.Success(response.DigestMarkdown.Trim());
    }

    private Task TrackUsageAsync(
        Guid worldId, Guid userId, AiUsage? usage, bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, userId, AiOperationType.CampaignRecap, usage,
            succeeded, errorCode, fallbackModel: _options.AiModel, ct: ct);

    internal const string GmSystemPrompt =
        """
        You maintain the GM's page for one campaign in a tabletop RPG world. From the
        campaign record in the user message, write the GM's story so far.

        Structure, in markdown:
        ## The story so far — how this campaign has actually unfolded, in order.
        ## Where things stand — the state the campaign is in now.
        ## Threads still open — unresolved tensions, sharpest first.

        Rules:
        - Ground every line in the record; invent nothing, and bring in nothing from
          outside it. This page is for the GM alone, so hidden and GM-only material
          belongs here — flag a secret the players are close to touching.
        - Write about this campaign only. The record may name people and places that
          outlive it; say what they did *here*.
        - 300 to 500 words. No preamble, no headings beyond the three above.
        - Weigh truth states: state Confirmed plainly, hedge Likely, attribute Rumor,
          name genuine Disputes instead of resolving them.

        Respond with a JSON object matching the structured output schema: {"digest": "..."}.
        """;

    internal const string PartySystemPrompt =
        """
        You maintain the players' page for one campaign in a tabletop RPG world. From the
        campaign record in the user message, write the recap the party would read before a
        session.

        Structure, in markdown:
        ## The story so far — a compact narrative of what the party knows has happened.
        ## Where things stand — the current state of the arcs and places that matter.
        ## Open questions — what the party knows it does not know.

        Rules:
        - Ground every line in the record; invent nothing. The record you are given is
          exactly what the party knows: never speculate about what else might exist, and
          never mention that anything is withheld or hidden.
        - Write about this campaign only. The record may name people and places that
          outlive it; say what they did *here*.
        - 250 to 400 words, in-world register, no meta-commentary about records or facts.
        - Weigh truth states: state Confirmed plainly, hedge Likely, attribute Rumor
          ("rumor has it…"), and present Disputes as open disagreements.

        Respond with a JSON object matching the structured output schema: {"digest": "..."}.
        """;
}
