using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// What Bibliotaph's Nornis storage provider may read (feature 31, Requirement 5): the worlds a
/// user created, never a demo or template, and their uploads that pass
/// <see cref="Models.BibliotaphExposure"/>. Pages are ordered by id and start after
/// <c>afterId</c>, the last id of the page before.
/// </summary>
public interface IBibliotaphProviderRepository
{
    Task<IReadOnlyList<World>> ListWorldsAsync(Guid userId, Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The world when the user created it and it is neither a demo nor a template, else null.</summary>
    Task<World?> GetWorldAsync(Guid userId, Guid worldId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryDocument>> ListFilesAsync(Guid worldId, Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The upload when it is exposed and in a world <see cref="GetWorldAsync"/> would return, else null.</summary>
    Task<LibraryDocument?> GetFileAsync(Guid userId, Guid fileId, CancellationToken cancellationToken = default);
}
