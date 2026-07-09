using FsCheck;
using FsCheck.NUnit;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

using Property = FsCheck.NUnit.PropertyAttribute;

namespace Nornis.Infrastructure.Tests.Persistence.PropertyTests;

/// <summary>
/// Property 4: Bidirectional Relationship Query
/// For any artifact that participates in relationships (as either ArtifactAId or ArtifactBId),
/// listing relationships for that artifact should return all relationships where the artifact
/// appears on either side, regardless of position.
///
/// **Validates: Requirements 7.6**
/// </summary>
[TestFixture]
public class BidirectionalRelationshipQueryTests : IntegrationTestBase
{
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) })]
    public async Task<bool> ListByArtifactAsync_Returns_Relationships_From_Both_Sides(
        ArtifactRelationship relationship1Template,
        ArtifactRelationship relationship2Template,
        ArtifactRelationship relationship3Template)
    {
        // Arrange: Create deterministic FK structure with random relationship data
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Auth0SubjectId = $"auth0|{userId:N}",
            Username = "Captain Voss",
            Email = "voss@blackharbor.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };

        var worldId = Guid.NewGuid();
        var world = new World
        {
            Id = worldId,
            Name = "Black Harbor Investigation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            RowVersion = []
        };

        // Create 3 artifacts: X (the target), Y, and Z
        var artifactX = CreateArtifact(worldId, "Silver Key");
        var artifactY = CreateArtifact(worldId, "Captain Voss");
        var artifactZ = CreateArtifact(worldId, "Black Harbor");

        // Relationship1: ArtifactX is on the A side
        var rel1 = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            ArtifactAId = artifactX.Id,
            ArtifactBId = artifactY.Id,
            Type = relationship1Template.Type,
            Description = relationship1Template.Description,
            Confidence = relationship1Template.Confidence,
            TruthState = relationship1Template.TruthState,
            Visibility = relationship1Template.Visibility,
            CreatedAt = relationship1Template.CreatedAt,
            UpdatedAt = relationship1Template.UpdatedAt,
            RowVersion = []
        };

        // Relationship2: ArtifactX is on the B side
        var rel2 = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            ArtifactAId = artifactZ.Id,
            ArtifactBId = artifactX.Id,
            Type = relationship2Template.Type,
            Description = relationship2Template.Description,
            Confidence = relationship2Template.Confidence,
            TruthState = relationship2Template.TruthState,
            Visibility = relationship2Template.Visibility,
            CreatedAt = relationship2Template.CreatedAt,
            UpdatedAt = relationship2Template.UpdatedAt,
            RowVersion = []
        };

        // Relationship3: Does NOT involve ArtifactX
        var rel3 = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            ArtifactAId = artifactY.Id,
            ArtifactBId = artifactZ.Id,
            Type = relationship3Template.Type,
            Description = relationship3Template.Description,
            Confidence = relationship3Template.Confidence,
            TruthState = relationship3Template.TruthState,
            Visibility = relationship3Template.Visibility,
            CreatedAt = relationship3Template.CreatedAt,
            UpdatedAt = relationship3Template.UpdatedAt,
            RowVersion = []
        };

        // Persist all entities
        Context.Users.Add(user);
        Context.Worlds.Add(world);
        Context.Artifacts.AddRange(artifactX, artifactY, artifactZ);
        Context.ArtifactRelationships.AddRange(rel1, rel2, rel3);
        await Context.SaveChangesAsync();

        // Act: Query relationships for ArtifactX using a fresh context
        using var queryContext = CreateNewContext();
        var repository = new ArtifactRelationshipRepository(queryContext);
        var results = await repository.ListByArtifactAsync(artifactX.Id);

        // Assert: Should return exactly rel1 and rel2, not rel3
        var resultIds = results.Select(r => r.Id).ToHashSet();

        var containsRel1 = resultIds.Contains(rel1.Id);
        var containsRel2 = resultIds.Contains(rel2.Id);
        var doesNotContainRel3 = !resultIds.Contains(rel3.Id);
        var exactlyTwo = results.Count == 2;

        // Clean up for next iteration (SQLite in-memory shares connection)
        Context.ArtifactRelationships.RemoveRange(Context.ArtifactRelationships);
        Context.Artifacts.RemoveRange(Context.Artifacts);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        return containsRel1 && containsRel2 && doesNotContainRel3 && exactlyTwo;
    }

    private static Artifact CreateArtifact(Guid worldId, string name) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        Type = ArtifactType.Item,
        Name = name,
        Visibility = VisibilityScope.PartyVisible,
        Status = ArtifactStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = []
    };
}
