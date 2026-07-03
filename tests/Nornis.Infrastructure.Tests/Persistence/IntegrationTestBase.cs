using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nornis.Infrastructure.Persistence;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// A test-specific DbContext that inherits from NornisDbContext but adjusts
/// the model for SQLite compatibility (removes nvarchar(max) column types).
/// </summary>
public class TestNornisDbContext : NornisDbContext
{
    public TestNornisDbContext(DbContextOptions<NornisDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQLite doesn't understand nvarchar(max). Remove explicit column types
        // that are SQL Server-specific.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var columnType = property.GetColumnType();
                if (columnType is not null &&
                    columnType.Contains("nvarchar(max)", StringComparison.OrdinalIgnoreCase))
                {
                    property.SetColumnType(null);
                }

                // SQLite doesn't support SQL Server rowversion. Make RowVersion
                // columns nullable and remove concurrency token behavior for SQLite.
                if (property.Name == "RowVersion" && property.ClrType == typeof(byte[]))
                {
                    property.IsNullable = true;
                    property.IsConcurrencyToken = false;
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                    property.SetDefaultValue(null);
                }
            }
        }
    }
}

/// <summary>
/// Base class for integration tests that provisions an in-memory SQLite database
/// with the NornisDbContext schema. SQLite doesn't support rowversion natively,
/// so concurrency tokens are treated as regular byte[] columns in this test context.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    protected NornisDbContext Context { get; }

    protected IntegrationTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = CreateDbContextOptions(_connection);
        Context = new TestNornisDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>
    /// Creates a fresh NornisDbContext sharing the same in-memory database connection.
    /// Useful for concurrency tests that need separate context instances.
    /// </summary>
    protected NornisDbContext CreateNewContext()
    {
        var options = CreateDbContextOptions(_connection);
        return new TestNornisDbContext(options);
    }

    private static DbContextOptions<NornisDbContext> CreateDbContextOptions(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<NornisDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Context.Dispose();
                _connection.Close();
                _connection.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
