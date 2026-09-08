namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Process-lifetime memory for <see cref="PendingMigrationsHealthCheck"/>: once the database has
/// been seen with no pending migrations, that answer holds for the rest of the process. The set
/// of migrations the code expects is fixed at build time, and the only way the schema moves is
/// forward — someone applies the missing ones — so "up to date" cannot become "behind" without a
/// new deploy, which is a new process.
///
/// It exists because the answer is expensive to keep re-deriving. The readiness probe asks
/// /health every ten seconds, and each ask was three round trips to SQL — by volume, the API's
/// single largest source of telemetry and its steadiest database load, all to re-learn a fact
/// that could not have changed.
///
/// The pending state is deliberately never remembered. That is the one case where the answer is
/// about to change — someone is running `dotnet ef database update` — and the very next probe
/// has to see it, because the deploy poll is waiting on exactly that.
/// </summary>
public sealed class MigrationStateMemo
{
    private volatile bool _upToDate;

    /// <summary>True once a check has seen the schema with nothing pending.</summary>
    public bool UpToDate => _upToDate;

    public void MarkUpToDate() => _upToDate = true;
}
