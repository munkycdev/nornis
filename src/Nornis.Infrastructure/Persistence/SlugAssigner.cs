using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Gives every tracked <see cref="ISlugged"/> row that has no slug one before it is saved.
///
/// Here, in the save path, rather than in the services that create artifacts, campaigns,
/// characters, sources and documents: there are eight creators today across review apply, the
/// demo clone, world import, uploads and the plain create commands, and the next one would not
/// know to call anything. A row without a slug is not wrong — links fall back to the id — but it
/// is a page with a GUID in its address, which is the thing this exists to end.
///
/// Uniqueness is per world and per kind. The base slug comes from <see cref="Slug.From"/>; a
/// collision with an existing row, or with another row in the same save, takes the next free
/// numeric suffix (<c>the-ashen-king-2</c>). The candidates are checked against every slug the
/// world already has for that kind, loaded in one query per kind per world — a world holds
/// hundreds of artifacts, not millions, and one string per row is cheaper than a round trip per
/// candidate. Two hosts saving the same name into the same world at the same instant can still
/// both pick the same suffix; the unique index turns that into a failed save rather than a
/// duplicate, which is the right side to fail on.
///
/// Only rows whose slug is null are touched. A rename never reassigns — see <see cref="ISlugged"/>.
/// </summary>
public static class SlugAssigner
{
    private static readonly ConcurrentDictionary<Type, Func<DbContext, Guid, CancellationToken, Task<HashSet<string>>>> Loaders = new();

    private static readonly MethodInfo LoadTakenMethod =
        typeof(SlugAssigner).GetMethod(nameof(LoadTakenOfKindAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static async Task AssignAsync(DbContext db, CancellationToken cancellationToken)
    {
        var pending = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Where(e => e.Entity is ISlugged { Slug: null })
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var group in pending.GroupBy(e => (Kind: e.Metadata.ClrType, ((ISlugged)e.Entity).WorldId)))
        {
            var taken = await LoadTakenAsync(db, group.Key.Kind, group.Key.WorldId, cancellationToken);
            foreach (var entry in group)
            {
                var entity = (ISlugged)entry.Entity;
                entity.Slug = NextFree(Slug.From(entity.SlugSource, group.Key.Kind.Name), taken);
            }
        }
    }

    /// <summary>The base slug if free, else the lowest numbered suffix that is; claims it in <paramref name="taken"/>.</summary>
    private static string NextFree(string baseSlug, HashSet<string> taken)
    {
        var candidate = baseSlug;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{baseSlug}-{n}";
        }

        return candidate;
    }

    private static Task<HashSet<string>> LoadTakenAsync(DbContext db, Type kind, Guid worldId, CancellationToken cancellationToken)
    {
        var loader = Loaders.GetOrAdd(kind, static k =>
            LoadTakenMethod.MakeGenericMethod(k)
                .CreateDelegate<Func<DbContext, Guid, CancellationToken, Task<HashSet<string>>>>());
        return loader(db, worldId, cancellationToken);
    }

    private static async Task<HashSet<string>> LoadTakenOfKindAsync<TEntity>(DbContext db, Guid worldId, CancellationToken cancellationToken)
        where TEntity : class, ISlugged
    {
        var slugs = await db.Set<TEntity>()
            .AsNoTracking()
            .Where(e => e.WorldId == worldId && e.Slug != null)
            .Select(e => e.Slug!)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(slugs, StringComparer.Ordinal);
    }
}
