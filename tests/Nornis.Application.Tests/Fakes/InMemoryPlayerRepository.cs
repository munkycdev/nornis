using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Shares the character list with an <see cref="InMemoryCharacterRepository"/> so a merge
/// re-points the same rows the character tests read back — the conservation tests compare
/// the world's character set before and after, which a private copy could not prove.
/// </summary>
public class InMemoryPlayerRepository : IPlayerRepository
{
    private readonly List<Player> _players = [];
    private readonly InMemoryCharacterRepository _characters;

    public InMemoryPlayerRepository(InMemoryCharacterRepository characters)
    {
        _characters = characters;
    }

    public IReadOnlyList<Player> Players => _players.AsReadOnly();

    public void Seed(params Player[] players) => _players.AddRange(players);

    public Task<Player> CreateAsync(Player player, CancellationToken cancellationToken = default)
    {
        _players.Add(player);
        return Task.FromResult(player);
    }

    public Task<Player?> GetByIdAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_players.FirstOrDefault(p => p.Id == playerId));

    public Task<Player?> GetByMemberAsync(Guid worldMemberId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_players.FirstOrDefault(p => p.WorldMemberId == worldMemberId));

    public async Task<Player> GetOrCreateByMemberAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        var existing = await GetByMemberAsync(member.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        return await CreateAsync(new Player
        {
            Id = Guid.NewGuid(),
            WorldId = member.WorldId,
            WorldMemberId = member.Id,
            Name = MemberDisplayName.For(member),
            CreatedAt = now,
            UpdatedAt = now,
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Player>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Player>>(_players.Where(p => p.WorldId == worldId).OrderBy(p => p.Name).ToList());

    public Task<Player> UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        var index = _players.FindIndex(p => p.Id == player.Id);
        if (index >= 0)
        {
            _players[index] = player;
        }

        return Task.FromResult(player);
    }

    public Task DeleteAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        _players.RemoveAll(p => p.Id == playerId);
        return Task.CompletedTask;
    }

    public Task MoveCharactersAndDeleteAsync(Guid fromPlayerId, Guid toPlayerId, CancellationToken cancellationToken = default)
    {
        foreach (var character in _characters.Characters.Where(c => c.PlayerId == fromPlayerId))
        {
            character.PlayerId = toPlayerId;
        }

        _players.RemoveAll(p => p.Id == fromPlayerId);
        return Task.CompletedTask;
    }
}
