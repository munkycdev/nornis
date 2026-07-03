using Nornis.Application.Authorization;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CampaignService : ICampaignService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly ICampaignMemberRepository _campaignMemberRepository;

    public CampaignService(
        ICampaignRepository campaignRepository,
        ICampaignMemberRepository campaignMemberRepository)
    {
        _campaignRepository = campaignRepository;
        _campaignMemberRepository = campaignMemberRepository;
    }

    public async Task<AppResult<Campaign>> CreateAsync(CreateCampaignCommand command, CancellationToken ct)
    {
        var nameValidation = ValidateName(command.Name);
        if (nameValidation is not null)
        {
            return AppResult<Campaign>.Fail(nameValidation);
        }

        var now = DateTimeOffset.UtcNow;

        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            GameSystem = command.GameSystem,
            CreatedByUserId = command.CreatingUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        campaign = await _campaignRepository.CreateAsync(campaign, ct);

        var member = new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            UserId = command.CreatingUserId,
            Role = CampaignRole.GM,
            JoinedAt = now
        };

        await _campaignMemberRepository.CreateAsync(member, ct);

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult<Campaign>> GetByIdAsync(Guid campaignId, Guid requestingUserId, CancellationToken ct)
    {
        var member = await _campaignMemberRepository.GetByCampaignAndUserAsync(campaignId, requestingUserId, ct);

        if (member is null)
        {
            return AppResult<Campaign>.Fail(new AppError(403, "access_denied", "You are not a member of this campaign."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);

        if (campaign is null)
        {
            return AppResult<Campaign>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult<Campaign>> UpdateAsync(UpdateCampaignCommand command, CancellationToken ct)
    {
        var member = await _campaignMemberRepository.GetByCampaignAndUserAsync(command.CampaignId, command.ActingUserId, ct);

        if (member is null || !member.Role.IsAtLeast(CampaignRole.GM))
        {
            return AppResult<Campaign>.Fail(new AppError(403, "insufficient_role", "Only a GM can update campaign settings."));
        }

        var campaign = await _campaignRepository.GetByIdAsync(command.CampaignId, ct);

        if (campaign is null)
        {
            return AppResult<Campaign>.Fail(new AppError(404, "not_found", "Campaign not found."));
        }

        if (command.Name is not null)
        {
            var nameValidation = ValidateName(command.Name);
            if (nameValidation is not null)
            {
                return AppResult<Campaign>.Fail(nameValidation);
            }

            campaign.Name = command.Name;
        }

        if (command.Description is not null)
        {
            campaign.Description = command.Description;
        }

        if (command.GameSystem is not null)
        {
            campaign.GameSystem = command.GameSystem;
        }

        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        campaign = await _campaignRepository.UpdateAsync(campaign, ct);

        return AppResult<Campaign>.Success(campaign);
    }

    public async Task<AppResult<IReadOnlyList<CampaignWithRoleDto>>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var campaigns = await _campaignRepository.ListByUserAsync(userId, ct);

        var result = new List<CampaignWithRoleDto>();

        foreach (var campaign in campaigns)
        {
            var member = await _campaignMemberRepository.GetByCampaignAndUserAsync(campaign.Id, userId, ct);
            if (member is not null)
            {
                result.Add(new CampaignWithRoleDto(campaign, member.Role));
            }
        }

        return AppResult<IReadOnlyList<CampaignWithRoleDto>>.Success(result);
    }

    private static AppError? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AppError(400, "validation_error", "Campaign name must not be empty or whitespace.");
        }

        if (name.Length > 100)
        {
            return new AppError(400, "validation_error", "Campaign name must be between 1 and 100 characters.");
        }

        return null;
    }
}
