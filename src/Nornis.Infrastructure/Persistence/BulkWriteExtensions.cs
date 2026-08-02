using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Set-based writes that work on both providers this codebase runs against.
///
/// <c>ExecuteDelete</c> and <c>ExecuteUpdate</c> need a relational provider; the API
/// integration tests run on InMemory, where they throw. Repositories had three answers to
/// that — a hand-written <c>IsRelational()</c> branch, a tracked load that always paid for
/// itself even against SQL Server, and an unguarded call that would simply throw if a test
/// ever reached it. The third is the dangerous one, because it looks like the first two
/// until the day something calls it.
///
/// These take the branch once. The relational path is the real bulk statement; the fallback
/// is a tracked load, which is correct rather than fast and only ever runs in tests.
/// </summary>
internal static class BulkWriteExtensions
{
    /// <summary>
    /// Deletes every matching row and persists. Whole operation, not a staged one — callers
    /// needing several deletes inside one transaction should open the transaction themselves,
    /// as <see cref="Repositories.LibraryChunkRepository"/> does.
    /// </summary>
    public static async Task DeleteWhereAsync<TEntity>(
        this NornisDbContext context,
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (context.Database.IsRelational())
        {
            await context.Set<TEntity>().Where(predicate).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var rows = await context.Set<TEntity>().Where(predicate).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return;
        }

        context.Set<TEntity>().RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets one property on every matching row and persists. Single property on purpose: the
    /// relational and in-memory halves have to mean the same thing, and a general setter
    /// expression cannot be replayed against loaded entities without the caller writing the
    /// assignment a second time — two statements of one intent, free to drift.
    /// </summary>
    public static async Task SetWhereAsync<TEntity, TProperty>(
        this NornisDbContext context,
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, TProperty>> property,
        TProperty value,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (context.Database.IsRelational())
        {
            await context.Set<TEntity>()
                .Where(predicate)
                .ExecuteUpdateAsync(setters => setters.SetProperty(property, value), cancellationToken);
            return;
        }

        var rows = await context.Set<TEntity>().Where(predicate).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return;
        }

        var member = (PropertyInfo)((MemberExpression)property.Body).Member;
        foreach (var row in rows)
        {
            member.SetValue(row, value);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
