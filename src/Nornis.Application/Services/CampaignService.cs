using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CampaignService : ICampaignService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly ICampaignRecapRepository _recapRepository;
    private readonly IWorldRepository _worldRepository;

    public CampaignService(
        ICampaignRepository campaignRepository,
        ICharacterRepository characterRepository,
        ISourceRepository sourceRepository,
        ICampaignRecapRepository recapRepository,
        IWorldRepository worldRepository)
    {
        _campaignRepository = campaignRepository;
        _characterRepository = characterRepository;
        _sourceRepository = sourceRepository;
        _recapRepository = recapRepository;
        _worldRepository = worldRepository;
    }

    public async Task<AppResult<Campaign>> CreateAsync(CreateCampaignCommand command, CancellationToken ct)
    {
        if (command.CreatingUserRole != WorldRole.GM)
        {
            return AppResult<Campaign>.Fail(new AppError(403, "insufficient_role", "Only GMs can create campaigns."));
        }

        var nameError = ValidateName(command.Name);
        if (nameError is not null)
        {
            return AppResult<Campaign>.Fail(nameError);
        }

        var dateError = ValidateDates(command.StartedAt, command.EndedAt);
        if (dateError is not null)
        {
            return AppResult<Campaign>.Fail(dateError);
        }

        var now = DateTimeOffset.UtcNow;

        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Name = command.Name.Trim(),
            Description = command.Description,
            Status = command.Status,
            StartedAt = command.StartedAt,
            EndedAt = command.EndedAt,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = command.CreatingUserId
        };

        campaign = await _campaignRepository.CreateAsync(campaign, ct);

        // A world with nothing current adopts its first active campaign. Almost every world
        // runs one campaign at a time, and asking those GMs to go and declare the obvious
        // would leave the common case defaulting to "no campaign" — which is the state this
        // whole pointer exists to stop being the default.
        if (campaign.Status == CampaignStatus.Active)
        {
            var world = await _worldRepository.GetByIdAsync(command.WorldId, ct);
            if (world is not null && world.CurrentCampaignId is null)
            {
                await _worldRepository.SetCurrentCampaignAsync(command.WorldId, campaign.Id, ct);
            }
        }

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult<Campaign>> GetByIdAsync(Guid campaignId, Guid worldId, CancellationToken ct)
    {
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);

        if (campaign is null || campaign.WorldId != worldId)
        {
            return AppResult<Campaign>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult<IReadOnlyList<Campaign>>> ListByWorldAsync(Guid worldId, CancellationToken ct)
    {
        var campaigns = await _campaignRepository.ListByWorldAsync(worldId, ct);
        return AppResult<IReadOnlyList<Campaign>>.Success(campaigns);
    }

    /// <summary>
    /// How many evidenced artifacts the page carries. The rollup over a long campaign can run
    /// to the whole codex; the page wants the cast, not the census, and reports the total it
    /// was cut from so a short list never reads as a complete one.
    /// </summary>
    public const int MaxRollupArtifacts = 120;

    /// <summary>How many sessions the page lists. The full count is reported alongside.</summary>
    public const int MaxRecentSessions = 25;

    public async Task<AppResult<CampaignDetail>> GetDetailAsync(
        Guid campaignId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);

        if (campaign is null || campaign.WorldId != worldId)
        {
            return AppResult<CampaignDetail>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        var filter = VisibilityFilter.ForRole(role, actingUserId);

        var characters = await _characterRepository.ListByCampaignAsync(campaignId, ct);
        var rollup = await _campaignRepository.GetRollupAsync(worldId, campaignId, filter, MaxRollupArtifacts, ct);

        // The sources list view's own query, campaign-scoped: visibility already applied in
        // SQL, and no Body/DerivedText columns for a page that shows neither.
        var sessions = await _sourceRepository.ListSummariesByWorldAsync(
            worldId, actingUserId, role, campaignId, cancellationToken: ct);

        var dated = sessions.Select(s => s.OccurredAt).OfType<DateTimeOffset>().ToList();

        var recap = await _recapRepository.GetByCampaignAsync(campaignId, ct);

        return AppResult<CampaignDetail>.Success(new CampaignDetail(
            campaign,
            characters,
            rollup,
            sessions.Take(MaxRecentSessions).ToList(),
            sessions.Count,
            dated.Count > 0 ? dated.Min() : null,
            dated.Count > 0 ? dated.Max() : null,
            CampaignRecapView.From(recap, role)));
    }

    public async Task<AppResult<IReadOnlyList<Campaign>>> ReorderAsync(
        Guid worldId, IReadOnlyList<Guid> orderedCampaignIds, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<IReadOnlyList<Campaign>>.Fail(
                new AppError(403, "insufficient_role", "Only GMs can reorder campaigns."));
        }

        // Ids from another world would silently do nothing in the repository; refusing is
        // the honest answer, and it catches a client sending a stale world's order.
        var worldCampaignIds = (await _campaignRepository.ListByWorldAsync(worldId, ct))
            .Select(c => c.Id)
            .ToHashSet();

        if (orderedCampaignIds.Any(id => !worldCampaignIds.Contains(id)))
        {
            return AppResult<IReadOnlyList<Campaign>>.Fail(new AppError(400, "invalid_campaign",
                "One or more campaigns do not exist in this world."));
        }

        if (orderedCampaignIds.Distinct().Count() != orderedCampaignIds.Count)
        {
            return AppResult<IReadOnlyList<Campaign>>.Fail(new AppError(400, "validation_error",
                "The campaign order must not repeat a campaign."));
        }

        var reordered = await _campaignRepository.ReorderAsync(worldId, orderedCampaignIds, ct);
        return AppResult<IReadOnlyList<Campaign>>.Success(reordered);
    }

    public async Task<AppResult<Campaign>> UpdateAsync(UpdateCampaignCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Campaign>.Fail(new AppError(403, "insufficient_role", "Only GMs can update campaigns."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(command.CampaignId, ct);

        if (campaign is null || campaign.WorldId != command.WorldId)
        {
            return AppResult<Campaign>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        if (command.Name is not null)
        {
            var nameError = ValidateName(command.Name);
            if (nameError is not null)
            {
                return AppResult<Campaign>.Fail(nameError);
            }

            campaign.Name = command.Name.Trim();
        }

        if (command.Description is not null)
        {
            campaign.Description = command.Description;
        }

        if (command.Status is not null)
        {
            campaign.Status = command.Status.Value;
        }

        // Null leaves a date alone; clearing is said explicitly, so "unset" and "remove" are
        // two values rather than one null meaning both.
        if (command.ClearStartedAt)
        {
            campaign.StartedAt = null;
        }
        else if (command.StartedAt is not null)
        {
            campaign.StartedAt = command.StartedAt;
        }

        if (command.ClearEndedAt)
        {
            campaign.EndedAt = null;
        }
        else if (command.EndedAt is not null)
        {
            campaign.EndedAt = command.EndedAt;
        }

        var dateError = ValidateDates(campaign.StartedAt, campaign.EndedAt);
        if (dateError is not null)
        {
            return AppResult<Campaign>.Fail(dateError);
        }

        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        campaign = await _campaignRepository.UpdateAsync(campaign, ct);

        // "Current" means the run of play in progress, so a campaign that has been completed
        // or archived cannot stay it. Clearing here rather than reading around it later keeps
        // one meaning for the pointer: whatever it names is Active.
        if (campaign.Status != CampaignStatus.Active)
        {
            var world = await _worldRepository.GetByIdAsync(command.WorldId, ct);
            if (world?.CurrentCampaignId == campaign.Id)
            {
                await _worldRepository.SetCurrentCampaignAsync(command.WorldId, null, ct);
            }
        }

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult<Campaign>> SetCurrentAsync(
        Guid campaignId, Guid worldId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<Campaign>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can choose the current campaign."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);

        if (campaign is null || campaign.WorldId != worldId)
        {
            return AppResult<Campaign>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        // The pointer's one invariant: whatever it names is Active. Refusing here is the only
        // reason readers never have to check the status of the campaign it hands them.
        if (campaign.Status != CampaignStatus.Active)
        {
            return AppResult<Campaign>.Fail(new AppError(409, "campaign_not_active",
                $"'{campaign.Name}' is {campaign.Status}. Only an active campaign can be the current one."));
        }

        await _worldRepository.SetCurrentCampaignAsync(worldId, campaignId, ct);

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult> ClearCurrentAsync(Guid worldId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role",
                "Only GMs can choose the current campaign."));
        }

        await _worldRepository.SetCurrentCampaignAsync(worldId, null, ct);

        return AppResult.Success();
    }

    public async Task<AppResult> DeleteAsync(Guid campaignId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Only GMs can delete campaigns."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);

        if (campaign is null || campaign.WorldId != worldId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        // Sources revert to "no campaign" and assignments are removed; knowledge is
        // never deleted with a campaign.
        await _campaignRepository.DeleteAsync(campaignId, ct);

        return AppResult.Success();
    }

    public async Task<AppResult<IReadOnlyList<Character>>> AssignCharactersAsync(AssignCampaignCharactersCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<IReadOnlyList<Character>>.Fail(new AppError(403, "insufficient_role", "Only GMs can assign characters to campaigns."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(command.CampaignId, ct);

        if (campaign is null || campaign.WorldId != command.WorldId)
        {
            return AppResult<IReadOnlyList<Character>>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        // Every character must exist in the same world as the campaign.
        var distinctIds = command.CharacterIds.Distinct().ToList();
        var worldCharacters = await _characterRepository.ListByWorldAsync(command.WorldId, ct);
        var validIds = worldCharacters.Select(c => c.Id).ToHashSet();

        var invalid = distinctIds.Where(id => !validIds.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            return AppResult<IReadOnlyList<Character>>.Fail(new AppError(400, "invalid_character",
                "One or more characters do not exist in this world."));
        }

        await _characterRepository.ReplaceCampaignAssignmentsAsync(command.CampaignId, distinctIds, ct);

        var assigned = await _characterRepository.ListByCampaignAsync(command.CampaignId, ct);
        return AppResult<IReadOnlyList<Character>>.Success(assigned);
    }

    private static AppError? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AppError(400, "validation_error", "Campaign name must not be empty or whitespace.");
        }

        if (name.Trim().Length > 200)
        {
            return new AppError(400, "validation_error", "Campaign name must be between 1 and 200 characters.");
        }

        return null;
    }

    private static AppError? ValidateDates(DateTimeOffset? startedAt, DateTimeOffset? endedAt)
    {
        if (startedAt is not null && endedAt is not null && endedAt < startedAt)
        {
            return new AppError(400, "validation_error", "Campaign end date must not be before its start date.");
        }

        return null;
    }
}
