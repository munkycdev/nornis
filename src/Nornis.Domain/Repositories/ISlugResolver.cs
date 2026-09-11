using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Turns the key in a detail-page URL into an id. The key is a slug, or a GUID for links that
/// predate slugs (bookmarks, Ask history, anything pasted before the change) — those keep
/// working forever, because a permalink that stops resolving is a broken promise. A GUID key is
/// returned as-is without a lookup: the service the caller hands it to already scopes by world
/// and answers 404 for a stranger, exactly as it did before slugs existed.
/// </summary>
public interface ISlugResolver
{
    /// <summary>Null when the key is a slug nobody in this world has.</summary>
    Task<Guid?> ResolveAsync<TEntity>(Guid worldId, string key, CancellationToken cancellationToken = default)
        where TEntity : class, ISlugged;
}
