using Microsoft.EntityFrameworkCore;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// The missing-row contract, decided once per verb.
///
/// **A mutation of a row that is not there throws.** Every caller reaches these having just
/// loaded and authorized the row, so its absence is a concurrent delete or a bug — never an
/// ordinary outcome. Returning quietly makes the request report success for work it did not
/// do, which is the failure mode the error-handling pass spent a day removing elsewhere.
///
/// **A delete of a row that is not there does nothing.** Delete is idempotent by nature: the
/// caller wanted the row gone and it is gone. Those sites stay as they are.
///
/// Repositories had all three answers — a throw with a useful message, a silent return, and
/// a bare <c>FirstAsync</c> whose exception says "Sequence contains no elements" and names
/// neither the entity nor the id.
/// </summary>
internal static class RequiredRowExtensions
{
    /// <summary>
    /// Loads a row by primary key for mutation, throwing if it is gone. Tracked on purpose —
    /// these callers are about to change a column — and via <c>FindAsync</c>, so a row already
    /// tracked in this scope is reused rather than attached a second time.
    /// </summary>
    public static async Task<TEntity> LoadForUpdateAsync<TEntity>(
        this NornisDbContext context, Guid id, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await context.Set<TEntity>().FindAsync([id], cancellationToken);

        return entity ?? throw new InvalidOperationException(
            $"{typeof(TEntity).Name} with id '{id}' not found.");
    }
}
