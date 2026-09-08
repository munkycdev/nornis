using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The world-delete 500 of 2026-07-25 happened because a deploy shipped code referencing
/// ExtractionReplays before the migration was applied — a gap no schema-from-model test
/// (EnsureCreated) can see, because those schemas are never behind the code. These tests
/// pin the /health guard instead: a database whose migration history is behind the
/// migrations assembly must report Unhealthy.
///
/// The second half pins the memo: a healthy answer is remembered for the process and a
/// pending one never is. The memo exists so the readiness probe stops re-querying a fact
/// that cannot change, and the tests here are what keep it from also hiding the one fact
/// that can — a migration being applied while the app waits for it.
/// </summary>
[TestFixture]
public class PendingMigrationsHealthCheckTests
{
    private SqliteConnection _connection = null!;
    private NornisDbContext _context = null!;
    private MigrationStateMemo _memo = null!;

    // NUnit runs every test in a fixture on one instance, so the connection must be
    // per-test: migration history written by one test would leak into the next.
    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // The real NornisDbContext, not TestNornisDbContext: the migrations assembly is
        // resolved from the context's assembly, and the check only reads the history
        // table — no SQL Server-specific DDL ever runs.
        var options = new DbContextOptionsBuilder<NornisDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new NornisDbContext(options);
        _memo = new MigrationStateMemo();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private Task<HealthCheckResult> CheckAsync() =>
        new PendingMigrationsHealthCheck(_context, _memo).CheckHealthAsync(new HealthCheckContext());

    private async Task RecordEveryMigrationAsAppliedAsync()
    {
        var history = _context.GetService<IHistoryRepository>();
        await _context.Database.ExecuteSqlRawAsync(history.GetCreateScript());
        foreach (var migrationId in _context.Database.GetMigrations())
        {
            await _context.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrationId, "10.0.2")));
        }
    }

    [Test]
    public async Task DatabaseBehindMigrationsAssembly_ReportsUnhealthy()
    {
        // A database with no migration history at all — the extreme version of the
        // prod incident, where one migration was missing.
        var result = await CheckAsync();

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        Assert.That(result.Description, Does.Contain("AddExtractionReplay"),
            "the failing migration should be named so the alert says what to run");
    }

    [Test]
    public async Task AllMigrationsRecordedAsApplied_ReportsHealthy()
    {
        await RecordEveryMigrationAsAppliedAsync();

        var result = await CheckAsync();

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    #region Memo

    [Test]
    public async Task HealthyAnswer_IsRemembered()
    {
        await RecordEveryMigrationAsAppliedAsync();
        await CheckAsync();

        Assert.That(_memo.UpToDate, Is.True);
    }

    [Test]
    public async Task UnhealthyAnswer_IsNotRemembered()
    {
        await CheckAsync();

        Assert.That(_memo.UpToDate, Is.False,
            "a pending state must be re-checked on every probe — it is the one that is about to change");
    }

    [Test]
    public async Task OnceRemembered_DatabaseIsNotAskedAgain()
    {
        await RecordEveryMigrationAsAppliedAsync();
        await CheckAsync();

        // Take the history table away. If the check went back to the database it would now
        // report every migration as pending; the memo is what keeps it Healthy.
        await _context.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\"");

        var result = await CheckAsync();

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task MigrationAppliedAfterAPendingAnswer_NextProbeSeesIt()
    {
        // The deploy poll's whole reason to exist: the first probe finds the schema behind,
        // someone runs `dotnet ef database update`, and the next probe must go green.
        var before = await CheckAsync();
        await RecordEveryMigrationAsAppliedAsync();
        var after = await CheckAsync();

        Assert.That(before.Status, Is.EqualTo(HealthStatus.Unhealthy));
        Assert.That(after.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    #endregion
}
