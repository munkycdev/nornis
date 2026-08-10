using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCharacterSheetSnapshotRepository : ICharacterSheetSnapshotRepository
{
    private readonly List<CharacterSheetSnapshot> _snapshots = [];

    public IReadOnlyList<CharacterSheetSnapshot> Snapshots => _snapshots.AsReadOnly();

    public void Seed(params CharacterSheetSnapshot[] snapshots) => _snapshots.AddRange(snapshots);

    public Task<IReadOnlyList<CharacterSheetSnapshot>> ListByCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var result = _snapshots
            .Where(s => s.CharacterId == characterId)
            .OrderByDescending(s => s.AsOf)
            .ThenByDescending(s => s.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<CharacterSheetSnapshot>>(result.AsReadOnly());
    }

    public Task<CharacterSheetSnapshot?> GetByIdAsync(Guid snapshotId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshots.FirstOrDefault(s => s.Id == snapshotId));

    public Task<CharacterSheetSnapshot> CreateAsync(
        CharacterSheetSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _snapshots.Add(snapshot);
        return Task.FromResult(snapshot);
    }

    // Delete of a row that is not there does nothing — the repository contract's rule.
    public Task DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        _snapshots.RemoveAll(s => s.Id == snapshotId);
        return Task.CompletedTask;
    }

    public Task DeleteByCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        _snapshots.RemoveAll(s => s.CharacterId == characterId);
        return Task.CompletedTask;
    }
}
