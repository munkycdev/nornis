using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCampaignMemberRepository : ICampaignMemberRepository
{
    private readonly List<CampaignMember> _members = [];

    public IReadOnlyList<CampaignMember> Members => _members.AsReadOnly();

    public Task<CampaignMember> CreateAsync(CampaignMember member, CancellationToken cancellationToken = default)
    {
        _members.Add(member);
        return Task.FromResult(member);
    }

    public Task<CampaignMember?> GetByCampaignAndUserAsync(Guid campaignId, Guid userId, CancellationToken cancellationToken = default)
    {
        var member = _members.FirstOrDefault(m => m.CampaignId == campaignId && m.UserId == userId);
        return Task.FromResult(member);
    }

    public Task<IReadOnlyList<CampaignMember>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var members = _members.Where(m => m.CampaignId == campaignId).ToList();
        return Task.FromResult<IReadOnlyList<CampaignMember>>(members.AsReadOnly());
    }

    public Task RemoveAsync(CampaignMember member, CancellationToken cancellationToken = default)
    {
        _members.RemoveAll(m => m.Id == member.Id);
        return Task.CompletedTask;
    }

    public Task<CampaignMember> UpdateAsync(CampaignMember member, CancellationToken cancellationToken = default)
    {
        var index = _members.FindIndex(m => m.Id == member.Id);
        if (index >= 0)
        {
            _members[index] = member;
        }
        return Task.FromResult(member);
    }

    public Task<int> CountByRoleAsync(Guid campaignId, CampaignRole role, CancellationToken cancellationToken = default)
    {
        var count = _members.Count(m => m.CampaignId == campaignId && m.Role == role);
        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<CampaignMember>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var members = _members.Where(m => m.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<CampaignMember>>(members.AsReadOnly());
    }
}
