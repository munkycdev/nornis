using FsCheck.NUnit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using static Nornis.Infrastructure.Tests.Persistence.EntityGenerators;

namespace Nornis.Infrastructure.Tests.Persistence.PropertyTests;

/// <summary>
/// Property 3: Optimistic Concurrency Detection
///
/// For any mutable entity (World, User, Artifact, ArtifactFact, ArtifactRelationship, ReviewProposal),
/// if two concurrent modifications are attempted against the same row version, the second save should
/// fail with a concurrency exception.
///
/// **Validates: Requirements 6.9**
///
/// Uses a ConcurrencyTestDbContext that configures RowVersion as IsConcurrencyToken with
/// ValueGeneratedNever, enabling proper concurrency detection on SQLite.
/// </summary>
[NUnit.Framework.TestFixture]
public class OptimisticConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly NornisDbContext _context;
    private bool _disposed;

    public OptimisticConcurrencyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = CreateDbContextOptions();
        _context = new ConcurrencyTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    private DbContextOptions<NornisDbContext> CreateDbContextOptions()
    {
        return new DbContextOptionsBuilder<NornisDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    private NornisDbContext CreateNewContext()
    {
        var options = CreateDbContextOptions();
        return new ConcurrencyTestDbContext(options);
    }

    private async Task<User> CreatePrerequisiteUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{Guid.NewGuid():N}",
            Username = "Captain Voss",
            Email = "voss@blackharbor.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = new byte[] { 0x01 }
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<World> CreatePrerequisiteWorldAsync(Guid userId)
    {
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor Investigation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            RowVersion = new byte[] { 0x01 }
        };
        _context.Worlds.Add(world);
        await _context.SaveChangesAsync();
        return world;
    }

    private async Task<Artifact> CreatePrerequisiteArtifactAsync(Guid worldId)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = ArtifactType.Character,
            Name = "Tavrin",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = new byte[] { 0x01 }
        };
        _context.Artifacts.Add(artifact);
        await _context.SaveChangesAsync();
        return artifact;
    }

    private async Task<Source> CreatePrerequisiteSourceAsync(Guid worldId, Guid userId)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        _context.Sources.Add(source);
        await _context.SaveChangesAsync();
        return source;
    }

    private async Task<ReviewBatch> CreatePrerequisiteReviewBatchAsync(Guid worldId, Guid sourceId)
    {
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ReviewBatches.Add(batch);
        await _context.SaveChangesAsync();
        return batch;
    }

    /// <summary>
    /// Simulates an optimistic concurrency conflict:
    /// 1. Load entity in Context B (gets current RowVersion)
    /// 2. Load in a separate context, change RowVersion, save (simulates concurrent modification)
    /// 3. Modify in Context B and attempt save — should throw DbUpdateConcurrencyException
    /// </summary>
    private async Task AssertConcurrencyConflict<TEntity>(
        Guid entityId,
        Action<TEntity> modifyB) where TEntity : class
    {
        using var contextB = CreateNewContext();
        var entityB = await contextB.Set<TEntity>().FindAsync(entityId);
        NUnit.Framework.Assert.That(entityB, NUnit.Framework.Is.Not.Null);

        // Simulate concurrent modification by another context
        using var contextInterference = CreateNewContext();
        var interferenceEntity = await contextInterference.Set<TEntity>().FindAsync(entityId);
        var entry = contextInterference.Entry(interferenceEntity!);
        entry.Property("RowVersion").CurrentValue = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 };
        entry.State = EntityState.Modified;
        await contextInterference.SaveChangesAsync();

        // Context B still has stale OriginalValue — save should fail
        modifyB(entityB!);

        NUnit.Framework.Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await contextB.SaveChangesAsync();
        });
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DomainArbitraries) })]
    public void WorldConcurrencyConflictIsDetected(World generated)
    {
        Task.Run(async () =>
        {
            var user = await CreatePrerequisiteUserAsync();

            var world = new World
            {
                Id = Guid.NewGuid(),
                Name = generated.Name,
                Description = generated.Description,
                GameSystem = generated.GameSystem,
                CreatedAt = generated.CreatedAt,
                UpdatedAt = generated.UpdatedAt,
                CreatedByUserId = user.Id,
                RowVersion = new byte[] { 0x01 }
            };
            _context.Worlds.Add(world);
            await _context.SaveChangesAsync();

            await AssertConcurrencyConflict<World>(
                world.Id,
                b => b.Name = "Modified by B");
        }).GetAwaiter().GetResult();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DomainArbitraries) })]
    public void UserConcurrencyConflictIsDetected(User generated)
    {
        Task.Run(async () =>
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Auth0SubjectId = $"auth0|{Guid.NewGuid():N}",
                Username = generated.Username,
                Email = generated.Email,
                CreatedAt = generated.CreatedAt,
                UpdatedAt = generated.UpdatedAt,
                RowVersion = new byte[] { 0x01 }
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await AssertConcurrencyConflict<User>(
                user.Id,
                b => b.Username = "Modified by B");
        }).GetAwaiter().GetResult();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DomainArbitraries) })]
    public void ArtifactConcurrencyConflictIsDetected(Artifact generated)
    {
        Task.Run(async () =>
        {
            var user = await CreatePrerequisiteUserAsync();
            var world = await CreatePrerequisiteWorldAsync(user.Id);

            var artifact = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                Type = generated.Type,
                Name = generated.Name,
                Summary = generated.Summary,
                Visibility = generated.Visibility,
                Confidence = generated.Confidence,
                Status = generated.Status,
                CreatedAt = generated.CreatedAt,
                UpdatedAt = generated.UpdatedAt,
                RowVersion = new byte[] { 0x01 }
            };
            _context.Artifacts.Add(artifact);
            await _context.SaveChangesAsync();

            await AssertConcurrencyConflict<Artifact>(
                artifact.Id,
                b => b.Name = "Modified by B");
        }).GetAwaiter().GetResult();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DomainArbitraries) })]
    public void ArtifactFactConcurrencyConflictIsDetected(ArtifactFact generated)
    {
        Task.Run(async () =>
        {
            var user = await CreatePrerequisiteUserAsync();
            var world = await CreatePrerequisiteWorldAsync(user.Id);
            var artifact = await CreatePrerequisiteArtifactAsync(world.Id);

            var fact = new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifact.Id,
                Predicate = generated.Predicate,
                Value = generated.Value,
                Confidence = generated.Confidence,
                TruthState = generated.TruthState,
                Visibility = generated.Visibility,
                CreatedAt = generated.CreatedAt,
                UpdatedAt = generated.UpdatedAt,
                RowVersion = new byte[] { 0x01 }
            };
            _context.ArtifactFacts.Add(fact);
            await _context.SaveChangesAsync();

            await AssertConcurrencyConflict<ArtifactFact>(
                fact.Id,
                b => b.Value = "Modified by B");
        }).GetAwaiter().GetResult();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DomainArbitraries) })]
    public void ArtifactRelationshipConcurrencyConflictIsDetected(ArtifactRelationship generated)
    {
        Task.Run(async () =>
        {
            var user = await CreatePrerequisiteUserAsync();
            var world = await CreatePrerequisiteWorldAsync(user.Id);

            var artifactA = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                Type = ArtifactType.Character,
                Name = "Artifact A",
                Visibility = VisibilityScope.PartyVisible,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                RowVersion = new byte[] { 0x01 }
            };
            var artifactB = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                Type = ArtifactType.Location,
                Name = "Artifact B",
                Visibility = VisibilityScope.PartyVisible,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                RowVersion = new byte[] { 0x01 }
            };
            _context.Artifacts.AddRange(artifactA, artifactB);
            await _context.SaveChangesAsync();

            var relationship = new ArtifactRelationship
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                ArtifactAId = artifactA.Id,
                ArtifactBId = artifactB.Id,
                Type = generated.Type,
                Description = generated.Description,
                Confidence = generated.Confidence,
                TruthState = generated.TruthState,
                Visibility = generated.Visibility,
                CreatedAt = generated.CreatedAt,
                UpdatedAt = generated.UpdatedAt,
                RowVersion = new byte[] { 0x01 }
            };
            _context.ArtifactRelationships.Add(relationship);
            await _context.SaveChangesAsync();

            await AssertConcurrencyConflict<ArtifactRelationship>(
                relationship.Id,
                b => b.Description = "Modified by B");
        }).GetAwaiter().GetResult();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DomainArbitraries) })]
    public void ReviewProposalConcurrencyConflictIsDetected(ReviewProposal generated)
    {
        Task.Run(async () =>
        {
            var user = await CreatePrerequisiteUserAsync();
            var world = await CreatePrerequisiteWorldAsync(user.Id);
            var source = await CreatePrerequisiteSourceAsync(world.Id, user.Id);
            var batch = await CreatePrerequisiteReviewBatchAsync(world.Id, source.Id);

            var proposal = new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batch.Id,
                ChangeType = generated.ChangeType,
                TargetType = generated.TargetType,
                TargetId = generated.TargetId,
                ProposedValueJson = generated.ProposedValueJson,
                Rationale = generated.Rationale,
                Confidence = generated.Confidence,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = generated.CreatedAt,
                RowVersion = new byte[] { 0x01 }
            };
            _context.ReviewProposals.Add(proposal);
            await _context.SaveChangesAsync();

            await AssertConcurrencyConflict<ReviewProposal>(
                proposal.Id,
                b => b.Rationale = "Modified by B");
        }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _context.Dispose();
                _connection.Close();
                _connection.Dispose();
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// DbContext that keeps RowVersion as a proper concurrency token for SQLite testing.
    /// </summary>
    private class ConcurrencyTestDbContext : NornisDbContext
    {
        public ConcurrencyTestDbContext(DbContextOptions<NornisDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
                    if (columnType is not null &&
                        columnType.Contains("datetimeoffset", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetColumnType(null);
                    }
                    if (property.Name == "RowVersion" && property.ClrType == typeof(byte[]))
                    {
                        property.IsConcurrencyToken = true;
                        property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                        property.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Save);
                        property.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Save);
                    }
                }
            }
        }
    }
}
