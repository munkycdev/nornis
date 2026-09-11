using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Extensions;

/// <summary>
/// The detail endpoints take a key — slug or GUID — where they used to take a GUID. This turns
/// the key into the id the existing service call wants, and answers a missing slug with the
/// same 404 the service gives a missing id, so the two paths are indistinguishable from outside.
/// </summary>
public static class SlugKeys
{
    public static async Task<AppResult<Guid>> ResolveKeyAsync<TEntity>(
        this ISlugResolver resolver, Guid worldId, string key, string kind, CancellationToken ct)
        where TEntity : class, ISlugged
    {
        var id = await resolver.ResolveAsync<TEntity>(worldId, key, ct);
        return id is { } found
            ? AppResult<Guid>.Success(found)
            : AppResult<Guid>.Fail(new AppError(404, "not_found", $"{kind} not found."));
    }
}
