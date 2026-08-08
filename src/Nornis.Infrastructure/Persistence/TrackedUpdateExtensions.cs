using Microsoft.EntityFrameworkCore;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Whole-entity writes that do not leave the instance tracked afterwards.
///
/// Repositories here read <c>AsNoTracking</c> and write with <c>Update(entity)</c>, which
/// attaches a fresh instance. EF keeps it tracked past <c>SaveChanges</c>, so a *second*
/// update of the same row inside one request attaches a different instance with the same
/// key and throws — deterministically, not as a race. A reveal whose facts and corrections
/// name the same fact hits it every time, as does the second of two accepted proposals
/// touching one artifact.
///
/// Detaching after the save is what makes each call the self-contained read-modify-write
/// the repository interface already implies. Nothing survives one call to collide with the
/// next.
/// </summary>
internal static class TrackedUpdateExtensions
{
    public static async Task SaveAndDetachAsync<TEntity>(
        this NornisDbContext context, TEntity entity, CancellationToken cancellationToken)
        where TEntity : class
    {
        context.Set<TEntity>().Update(entity);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(entity).State = EntityState.Detached;
    }

    /// <summary>
    /// The same contract for an insert. An <c>Add</c> leaves the instance tracked too, so a
    /// request that inserts a row and then updates it — a review accept that has to create the
    /// artifact a proposal names, and then accepts the Create it just wrote — attaches a
    /// second instance of the same key and throws.
    /// </summary>
    public static async Task AddAndDetachAsync<TEntity>(
        this NornisDbContext context, TEntity entity, CancellationToken cancellationToken)
        where TEntity : class
    {
        context.Set<TEntity>().Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(entity).State = EntityState.Detached;
    }

    public static async Task SaveAndDetachRangeAsync<TEntity>(
        this NornisDbContext context, IReadOnlyList<TEntity> entities, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (entities.Count == 0)
        {
            return;
        }

        context.Set<TEntity>().UpdateRange(entities);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var entity in entities)
        {
            context.Entry(entity).State = EntityState.Detached;
        }
    }
}
