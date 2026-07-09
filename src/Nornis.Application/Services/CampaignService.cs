using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CampaignService : ICampaignService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly ICharacterRepository _characterRepository;

    public CampaignService(ICampaignRepository campaignRepository, ICharacterRepository characterRepository)
    {
        _campaignRepository = campaignRepository;
        _characterRepository = characterRepository;
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

        if (command.StartedAt is not null)
        {
            campaign.StartedAt = command.StartedAt;
        }

        if (command.EndedAt is not null)
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

        return AppResult<Campaign>.Success(campaign);
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
