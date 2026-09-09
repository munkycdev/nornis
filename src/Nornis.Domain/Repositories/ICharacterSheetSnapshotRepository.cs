using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ICharacterSheetSnapshotRepository
{
    /// <summary>
    /// Every snapshot attached to the character, newest <c>AsOf</c> first. Unfiltered — the
    /// caller applies source visibility through <c>ISourceRepository.ListAttributionByIdsAsync</c>,
    /// which is the one place that rule lives.
    /// </summary>
    Task<IReadOnlyList<CharacterSheetSnapshot>> ListByCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetSnapshot?> GetByIdAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    Task<CharacterSheetSnapshot> CreateAsync(
        CharacterSheetSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>Removes every snapshot for a character. No cascade covers this — see
    /// <c>CharacterSheetSnapshotConfiguration</c> for why.</summary>
    Task DeleteByCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);
}
