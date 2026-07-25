using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Reports Unhealthy while the database is missing migrations the deployed code expects.
/// Migrations are applied manually before deploy (see deploy.yml); when that step is
/// missed, the app comes up fine and then 500s on the first query that touches a missing
/// table. This turns that silent gap into a failing /health, so the availability alert
/// fires at deploy time instead of a user finding it.
/// </summary>
public class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly NornisDbContext _context;

    public PendingMigrationsHealthCheck(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Test hosts run on non-relational providers with no concept of migrations.
        if (!_context.Database.IsRelational())
        {
            return HealthCheckResult.Healthy("Non-relational provider; migrations not applicable.");
        }

        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        return pending.Count == 0
            ? HealthCheckResult.Healthy("Database schema is up to date.")
            : HealthCheckResult.Unhealthy(
                $"Database is missing {pending.Count} migration(s): {string.Join(", ", pending)}");
    }
}
