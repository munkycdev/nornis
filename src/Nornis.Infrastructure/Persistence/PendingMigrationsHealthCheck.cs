using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Reports Unhealthy while the database is missing migrations the deployed code expects.
/// Migrations are applied manually before deploy (see deploy.yml); when that step is
/// missed, the app comes up fine and then 500s on the first query that touches a missing
/// table. This turns that silent gap into a failing /health, which the readiness probe and
/// the deploy poll both read — so the rollout stops instead of a user finding it.
///
/// The database is asked only until it answers "up to date"; after that the answer comes from
/// <see cref="MigrationStateMemo"/>, which explains why it is safe to stop asking.
/// </summary>
public class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly NornisDbContext _context;
    private readonly MigrationStateMemo _memo;

    public PendingMigrationsHealthCheck(NornisDbContext context, MigrationStateMemo memo)
    {
        _context = context;
        _memo = memo;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_memo.UpToDate)
        {
            return HealthCheckResult.Healthy("Database schema is up to date.");
        }

        // Test hosts run on non-relational providers with no concept of migrations.
        if (!_context.Database.IsRelational())
        {
            return HealthCheckResult.Healthy("Non-relational provider; migrations not applicable.");
        }

        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"Database is missing {pending.Count} migration(s): {string.Join(", ", pending)}");
        }

        _memo.MarkUpToDate();
        return HealthCheckResult.Healthy("Database schema is up to date.");
    }
}
