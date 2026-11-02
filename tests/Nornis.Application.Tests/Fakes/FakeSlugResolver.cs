using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>The real resolver's contract: an id resolves to itself, and a slug to whichever row the lookup finds.</summary>
public class FakeSlugResolver(Func<Guid, string, Guid?> lookUpSlug) : ISlugResolver
{
    public Task<Guid?> ResolveAsync<TEntity>(Guid worldId, string key, CancellationToken cancellationToken = default)
        where TEntity : class, ISlugged =>
        Task.FromResult(Guid.TryParse(key, out var id) ? id : lookUpSlug(worldId, key));
}
