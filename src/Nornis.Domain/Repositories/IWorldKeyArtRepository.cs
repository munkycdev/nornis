using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

/// <summary>
/// The key-art columns on a world (feature 42), written one targeted change at a time rather than
/// by a whole-world save, so a GM choosing art never loses a race with an unrelated settings edit
/// (and the reverse). Reading is <see cref="IWorldRepository.GetByIdAsync"/>, which brings them.
/// </summary>
public interface IWorldKeyArtRepository
{
    /// <summary>Sets the key art, or clears it with null.</summary>
    Task SetKeyArtAsync(Guid worldId, WorldKeyArt? keyArt, CancellationToken cancellationToken = default);

    /// <summary>Saves the working brief and replaces the options with <paramref name="optionIds"/>.</summary>
    Task SaveKeyArtWorkAsync(Guid worldId, string? brief, IReadOnlyList<Guid> optionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts <paramref name="count"/> images against <paramref name="month"/>'s cap before the call,
    /// in one conditional write so two generations at once can't both pass it. A world last counted
    /// in an earlier month (or never) starts again from zero in the same write. False when the
    /// images would pass <paramref name="cap"/>.
    /// </summary>
    Task<bool> TryCountKeyArtImagesAsync(Guid worldId, string month, int count, int cap, CancellationToken cancellationToken = default);

    /// <summary>Gives back images counted for a call that failed or delivered fewer, in the month they were counted.</summary>
    Task ReleaseKeyArtImagesAsync(Guid worldId, string month, int count, CancellationToken cancellationToken = default);
}
