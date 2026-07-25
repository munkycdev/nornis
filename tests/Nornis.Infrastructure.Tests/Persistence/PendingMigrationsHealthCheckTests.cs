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
/// </summary>
[TestFixture]
public class PendingMigrationsHealthCheckTests
{
    private SqliteConnection _connection = null!;
    private NornisDbContext _context = null!;

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
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private Task<HealthCheckResult> CheckAsync() =>
        new PendingMigrationsHealthCheck(_context).CheckHealthAsync(new HealthCheckContext());

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
        var history = _context.GetService<IHistoryRepository>();
        await _context.Database.ExecuteSqlRawAsync(history.GetCreateScript());
        foreach (var migrationId in _context.Database.GetMigrations())
        {
            await _context.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrationId, "10.0.2")));
        }

        var result = await CheckAsync();

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }
}
