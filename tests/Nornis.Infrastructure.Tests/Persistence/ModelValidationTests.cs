using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests that verify EF Core model metadata matches the design specification.
/// Uses a SQL Server-configured NornisDbContext (no actual connection) for model inspection,
/// and the SQLite-backed IntegrationTestBase for schema creation validation.
/// </summary>
[TestFixture]
public class ModelValidationTests : IntegrationTestBase
{
    /// <summary>
    /// Returns a NornisDbContext configured for SQL Server (no actual connection needed).
    /// This preserves the original model metadata (column types, concurrency tokens, etc.)
    /// without the SQLite compatibility modifications made by TestNornisDbContext.
    /// </summary>
    private static NornisDbContext CreateSqlServerModelContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<NornisDbContext>();
        optionsBuilder.UseSqlServer("Server=.;Database=Test;Trusted_Connection=True;TrustServerCertificate=True");
        return new NornisDbContext(optionsBuilder.Options);
    }

    #region Table Names

    [TestCase(typeof(User), "Users")]
    [TestCase(typeof(World), "Worlds")]
    [TestCase(typeof(WorldMember), "WorldMembers")]
    [TestCase(typeof(Source), "Sources")]
    [TestCase(typeof(SourceExtraction), "SourceExtractions")]
    [TestCase(typeof(Artifact), "Artifacts")]
    [TestCase(typeof(ArtifactFact), "ArtifactFacts")]
    [TestCase(typeof(ArtifactRelationship), "ArtifactRelationships")]
    [TestCase(typeof(SourceReference), "SourceReferences")]
    [TestCase(typeof(ReviewBatch), "ReviewBatches")]
    [TestCase(typeof(ReviewProposal), "ReviewProposals")]
    [TestCase(typeof(AiUsageRecord), "AiUsageRecords")]
    public void Entity_MapsToCorrectTableName(Type entityType, string expectedTableName)
    {
        using var context = CreateSqlServerModelContext();
        var entityTypeMetadata = context.Model.FindEntityType(entityType);

        Assert.That(entityTypeMetadata, Is.Not.Null, $"Entity type {entityType.Name} not found in model");
        Assert.That(entityTypeMetadata!.GetTableName(), Is.EqualTo(expectedTableName));
    }

    [Test]
    public void Model_Contains_All_Twelve_Entities()
    {
        using var context = CreateSqlServerModelContext();

        var expectedEntityTypes = new[]
        {
            typeof(User), typeof(World), typeof(WorldMember),
            typeof(Source), typeof(SourceExtraction), typeof(Artifact),
            typeof(ArtifactFact), typeof(ArtifactRelationship), typeof(SourceReference),
            typeof(ReviewBatch), typeof(ReviewProposal), typeof(AiUsageRecord)
        };

        foreach (var entityType in expectedEntityTypes)
        {
            var metadata = context.Model.FindEntityType(entityType);
            Assert.That(metadata, Is.Not.Null, $"Entity {entityType.Name} is missing from the model");
        }
    }

    #endregion

    #region Unique Indexes

    [Test]
    public void User_HasUniqueIndex_OnAuth0SubjectId()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(User))!;
        var indexes = entityType.GetIndexes().ToList();

        var auth0Index = indexes.FirstOrDefault(i =>
            i.Properties.Count == 1 &&
            i.Properties[0].Name == nameof(User.Auth0SubjectId));

        Assert.That(auth0Index, Is.Not.Null, "User should have an index on Auth0SubjectId");
        Assert.That(auth0Index!.IsUnique, Is.True, "Auth0SubjectId index should be unique");
    }

    [Test]
    public void WorldMember_HasUniqueCompositeIndex_OnWorldIdAndUserId()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(WorldMember))!;
        var indexes = entityType.GetIndexes().ToList();

        var compositeIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 2 &&
            i.Properties.Any(p => p.Name == nameof(WorldMember.WorldId)) &&
            i.Properties.Any(p => p.Name == nameof(WorldMember.UserId)));

        Assert.That(compositeIndex, Is.Not.Null, "WorldMember should have a composite index on (WorldId, UserId)");
        Assert.That(compositeIndex!.IsUnique, Is.True, "Composite index should be unique");
    }

    [Test]
    public void Source_HasIndex_OnWorldIdAndProcessingStatus()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(Source))!;
        var indexes = entityType.GetIndexes().ToList();

        var statusIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 2 &&
            i.Properties.Any(p => p.Name == nameof(Source.WorldId)) &&
            i.Properties.Any(p => p.Name == nameof(Source.ProcessingStatus)));

        Assert.That(statusIndex, Is.Not.Null, "Source should have an index on (WorldId, ProcessingStatus)");
    }

    [Test]
    public void ArtifactRelationship_HasIndexes_OnArtifactAIdAndArtifactBId()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ArtifactRelationship))!;
        var indexes = entityType.GetIndexes().ToList();

        var artifactAIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 1 &&
            i.Properties[0].Name == nameof(ArtifactRelationship.ArtifactAId));

        var artifactBIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 1 &&
            i.Properties[0].Name == nameof(ArtifactRelationship.ArtifactBId));

        Assert.That(artifactAIndex, Is.Not.Null, "ArtifactRelationship should have an index on ArtifactAId");
        Assert.That(artifactBIndex, Is.Not.Null, "ArtifactRelationship should have an index on ArtifactBId");
    }

    [Test]
    public void ReviewProposal_HasIndex_OnReviewBatchIdAndStatus()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ReviewProposal))!;
        var indexes = entityType.GetIndexes().ToList();

        var reviewIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 2 &&
            i.Properties.Any(p => p.Name == nameof(ReviewProposal.ReviewBatchId)) &&
            i.Properties.Any(p => p.Name == nameof(ReviewProposal.Status)));

        Assert.That(reviewIndex, Is.Not.Null, "ReviewProposal should have an index on (ReviewBatchId, Status)");
    }

    #endregion

    #region String Max Lengths

    [TestCase(typeof(User), nameof(User.Auth0SubjectId), 200)]
    [TestCase(typeof(User), nameof(User.Username), 200)]
    [TestCase(typeof(User), nameof(User.Email), 200)]
    [TestCase(typeof(World), nameof(World.Name), 200)]
    [TestCase(typeof(World), nameof(World.Description), 2000)]
    [TestCase(typeof(World), nameof(World.GameSystem), 200)]
    [TestCase(typeof(WorldMember), nameof(WorldMember.DisplayName), 200)]
    [TestCase(typeof(WorldMember), nameof(WorldMember.CharacterName), 200)]
    [TestCase(typeof(Source), nameof(Source.Title), 200)]
    [TestCase(typeof(Source), nameof(Source.Uri), 2000)]
    [TestCase(typeof(Artifact), nameof(Artifact.Name), 200)]
    [TestCase(typeof(Artifact), nameof(Artifact.Summary), 2000)]
    [TestCase(typeof(ArtifactFact), nameof(ArtifactFact.Predicate), 200)]
    [TestCase(typeof(ArtifactFact), nameof(ArtifactFact.Value), 2000)]
    [TestCase(typeof(ArtifactRelationship), nameof(ArtifactRelationship.Type), 200)]
    [TestCase(typeof(ArtifactRelationship), nameof(ArtifactRelationship.Description), 2000)]
    [TestCase(typeof(SourceReference), nameof(SourceReference.Quote), 2000)]
    [TestCase(typeof(SourceReference), nameof(SourceReference.Notes), 2000)]
    [TestCase(typeof(ReviewProposal), nameof(ReviewProposal.Rationale), 2000)]
    [TestCase(typeof(AiUsageRecord), nameof(AiUsageRecord.Model), 200)]
    [TestCase(typeof(AiUsageRecord), nameof(AiUsageRecord.ErrorCode), 200)]
    public void StringProperty_HasCorrectMaxLength(Type entityType, string propertyName, int expectedMaxLength)
    {
        using var context = CreateSqlServerModelContext();
        var entityTypeMetadata = context.Model.FindEntityType(entityType)!;
        var property = entityTypeMetadata.FindProperty(propertyName);

        Assert.That(property, Is.Not.Null, $"Property {propertyName} not found on {entityType.Name}");
        Assert.That(property!.GetMaxLength(), Is.EqualTo(expectedMaxLength),
            $"{entityType.Name}.{propertyName} should have max length {expectedMaxLength}");
    }

    [TestCase(typeof(Source), nameof(Source.Body))]
    [TestCase(typeof(SourceExtraction), nameof(SourceExtraction.Text))]
    [TestCase(typeof(ReviewProposal), nameof(ReviewProposal.ProposedValueJson))]
    public void StringProperty_IsNvarcharMax(Type entityType, string propertyName)
    {
        using var context = CreateSqlServerModelContext();
        var entityTypeMetadata = context.Model.FindEntityType(entityType)!;
        var property = entityTypeMetadata.FindProperty(propertyName);

        Assert.That(property, Is.Not.Null, $"Property {propertyName} not found on {entityType.Name}");

        var columnType = property!.GetColumnType();
        Assert.That(columnType, Is.EqualTo("nvarchar(max)"),
            $"{entityType.Name}.{propertyName} should be nvarchar(max) but was {columnType}");
    }

    #endregion

    #region Column Types (SQL Server)

    [TestCase(typeof(User), nameof(User.CreatedAt), "datetimeoffset")]
    [TestCase(typeof(User), nameof(User.UpdatedAt), "datetimeoffset")]
    [TestCase(typeof(World), nameof(World.CreatedAt), "datetimeoffset")]
    [TestCase(typeof(World), nameof(World.UpdatedAt), "datetimeoffset")]
    [TestCase(typeof(Source), nameof(Source.CreatedAt), "datetimeoffset")]
    [TestCase(typeof(Artifact), nameof(Artifact.CreatedAt), "datetimeoffset")]
    [TestCase(typeof(Artifact), nameof(Artifact.UpdatedAt), "datetimeoffset")]
    public void DateTimeOffsetProperty_HasCorrectColumnType(Type entityType, string propertyName, string expectedColumnType)
    {
        using var context = CreateSqlServerModelContext();
        var entityTypeMetadata = context.Model.FindEntityType(entityType)!;
        var property = entityTypeMetadata.FindProperty(propertyName);

        Assert.That(property, Is.Not.Null, $"Property {propertyName} not found on {entityType.Name}");
        Assert.That(property!.GetColumnType(), Is.EqualTo(expectedColumnType),
            $"{entityType.Name}.{propertyName} should have column type {expectedColumnType}");
    }

    #endregion

    #region Concurrency Tokens

    [TestCase(typeof(User))]
    [TestCase(typeof(World))]
    [TestCase(typeof(Artifact))]
    [TestCase(typeof(ArtifactFact))]
    [TestCase(typeof(ArtifactRelationship))]
    [TestCase(typeof(ReviewProposal))]
    public void MutableEntity_HasRowVersionConcurrencyToken(Type entityType)
    {
        // Use SQL Server model to inspect concurrency tokens (SQLite modifies these)
        using var context = CreateSqlServerModelContext();
        var entityTypeMetadata = context.Model.FindEntityType(entityType)!;
        var rowVersionProperty = entityTypeMetadata.FindProperty("RowVersion");

        Assert.That(rowVersionProperty, Is.Not.Null,
            $"{entityType.Name} should have a RowVersion property");
        Assert.That(rowVersionProperty!.IsConcurrencyToken, Is.True,
            $"{entityType.Name}.RowVersion should be configured as a concurrency token");
    }

    #endregion

    #region Foreign Key Delete Behaviors

    [Test]
    public void World_CreatedByUser_FK_HasRestrictDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(World))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(World.CreatedByUserId)));

        Assert.That(fk, Is.Not.Null, "World should have a FK on CreatedByUserId");
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Restrict));
    }

    [Test]
    public void WorldMember_World_FK_HasCascadeDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(WorldMember))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(WorldMember.WorldId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade));
    }

    [Test]
    public void WorldMember_User_FK_HasRestrictDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(WorldMember))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(WorldMember.UserId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Restrict));
    }

    [Test]
    public void Source_World_FK_HasCascadeDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(Source))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(Source.WorldId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade));
    }

    [Test]
    public void Source_CreatedByUser_FK_HasRestrictDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(Source))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(Source.CreatedByUserId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Restrict));
    }

    [Test]
    public void ArtifactRelationship_ArtifactA_FK_HasRestrictDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ArtifactRelationship))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(ArtifactRelationship.ArtifactAId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Restrict));
    }

    [Test]
    public void ArtifactRelationship_ArtifactB_FK_HasRestrictDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ArtifactRelationship))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(ArtifactRelationship.ArtifactBId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Restrict));
    }

    [Test]
    public void ArtifactRelationship_World_FK_HasCascadeDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ArtifactRelationship))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(ArtifactRelationship.WorldId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade));
    }

    [Test]
    public void ReviewBatch_Source_FK_HasRestrictDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ReviewBatch))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(ReviewBatch.SourceId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Restrict));
    }

    [Test]
    public void ReviewProposal_ReviewBatch_FK_HasCascadeDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(ReviewProposal))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(ReviewProposal.ReviewBatchId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade));
    }

    [Test]
    public void AiUsageRecord_World_FK_HasSetNullDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(AiUsageRecord))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(AiUsageRecord.WorldId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.SetNull));
    }

    [Test]
    public void AiUsageRecord_User_FK_HasSetNullDelete()
    {
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(AiUsageRecord))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(AiUsageRecord.UserId)));

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.DeleteBehavior, Is.EqualTo(DeleteBehavior.SetNull));
    }

    #endregion

    #region Migration Applies Cleanly

    [Test]
    public void EnsureCreated_Succeeds_OnFreshDatabase()
    {
        // The IntegrationTestBase already calls EnsureCreated() in its constructor.
        // This test confirms the schema can be created and queried on a fresh connection.
        using var freshContext = CreateNewContext();

        var canQuery = true;
        try
        {
            _ = freshContext.Users.ToList();
        }
        catch
        {
            canQuery = false;
        }

        Assert.That(canQuery, Is.True, "Schema should be queryable after EnsureCreated");
    }

    [Test]
    public void AllTables_AreAccessible_AfterSchemaCreation()
    {
        // Verify every DbSet is queryable (schema created correctly for all entities)
        Assert.DoesNotThrow(() => Context.Users.ToList());
        Assert.DoesNotThrow(() => Context.Worlds.ToList());
        Assert.DoesNotThrow(() => Context.WorldMembers.ToList());
        Assert.DoesNotThrow(() => Context.Sources.ToList());
        Assert.DoesNotThrow(() => Context.SourceExtractions.ToList());
        Assert.DoesNotThrow(() => Context.Artifacts.ToList());
        Assert.DoesNotThrow(() => Context.ArtifactFacts.ToList());
        Assert.DoesNotThrow(() => Context.ArtifactRelationships.ToList());
        Assert.DoesNotThrow(() => Context.SourceReferences.ToList());
        Assert.DoesNotThrow(() => Context.ReviewBatches.ToList());
        Assert.DoesNotThrow(() => Context.ReviewProposals.ToList());
        Assert.DoesNotThrow(() => Context.AiUsageRecords.ToList());
    }

    #endregion
}
