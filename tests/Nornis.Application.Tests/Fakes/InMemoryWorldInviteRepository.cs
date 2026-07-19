using Nornis.Domain.Entities;
using Nornis.Domain.Exceptions;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryWorldInviteRepository : IWorldInviteRepository
{
    private readonly List<WorldInvite> _invites = [];

    public IReadOnlyList<WorldInvite> Invites => _invites.AsReadOnly();

    /// <summary>
    /// When true, the next <see cref="UpdateAsync"/> throws <see cref="ConcurrencyConflictException"/>
    /// once, simulating a lost optimistic-concurrency race. Resets itself after firing.
    /// </summary>
    public bool ThrowConcurrencyOnNextUpdate { get; set; }

    public void Seed(WorldInvite invite) => _invites.Add(invite);

    public Task<WorldInvite> CreateAsync(WorldInvite invite, CancellationToken cancellationToken = default)
    {
        _invites.Add(invite);
        return Task.FromResult(invite);
    }

    public Task<WorldInvite?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var invite = _invites.FirstOrDefault(i => i.Code == code);
        return Task.FromResult(invite);
    }

    public Task<WorldInvite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invite = _invites.FirstOrDefault(i => i.Id == id);
        return Task.FromResult(invite);
    }

    public Task<IReadOnlyList<WorldInvite>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var invites = _invites
            .Where(i => i.WorldId == worldId)
            .OrderByDescending(i => i.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<WorldInvite>>(invites.AsReadOnly());
    }

    public Task<WorldInvite> UpdateAsync(WorldInvite invite, CancellationToken cancellationToken = default)
    {
        if (ThrowConcurrencyOnNextUpdate)
        {
            ThrowConcurrencyOnNextUpdate = false;
            throw new ConcurrencyConflictException("Simulated concurrency conflict.");
        }

        var index = _invites.FindIndex(i => i.Id == invite.Id);
        if (index >= 0)
        {
            _invites[index] = invite;
        }
        return Task.FromResult(invite);
    }
}
