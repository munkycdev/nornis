using FsCheck.NUnit;
using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence.PropertyTests;

/// <summary>
/// Property 2: Update Persistence
/// For any valid domain entity that has been persisted, modifying a mutable property
/// and calling the repository update method, then retrieving the entity by ID,
/// should reflect the modified value.
///
/// **Validates: Requirements 7.5**
/// </summary>
[TestFixture]
public class UpdatePersistenceTests : IntegrationTestBase
{
    [FsCheck.NUnit.Property(Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) }, MaxTest = 100)]
    public async Task<bool> World_Update_Name_Is_Persisted(World world)
    {
        // Arrange: create parent User to satisfy FK
        var user = CreateValidUser();
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        world.Id = Guid.NewGuid();
        world.CreatedByUserId = user.Id;
        world.RowVersion = [];

        var repo = new WorldRepository(Context);
        await repo.CreateAsync(world);

        // Act: mutate name
        var newName = "Updated " + world.Name;
        if (newName.Length > 200)
            newName = newName[..200];
        world.Name = newName;
        await repo.UpdateAsync(world);

        // Assert: retrieve with fresh context
        using var freshContext = CreateNewContext();
        var retrieved = await freshContext.Worlds
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == world.Id);

        return retrieved is not null && retrieved.Name == newName;
    }

    [FsCheck.NUnit.Property(Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) }, MaxTest = 100)]
    public async Task<bool> User_Update_Username_Is_Persisted(User user)
    {
        // Arrange
        user.Id = Guid.NewGuid();
        user.Auth0SubjectId = Guid.NewGuid().ToString(); // ensure unique
        user.RowVersion = [];

        var repo = new UserRepository(Context);
        await repo.CreateAsync(user);

        // Act: mutate username
        var newUsername = "Updated " + user.Username;
        if (newUsername.Length > 200)
            newUsername = newUsername[..200];
        user.Username = newUsername;
        await repo.UpdateAsync(user);

        // Assert
        using var freshContext = CreateNewContext();
        var retrieved = await freshContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        return retrieved is not null && retrieved.Username == newUsername;
    }

    [FsCheck.NUnit.Property(Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) }, MaxTest = 100)]
    public async Task<bool> Artifact_Update_Summary_Is_Persisted(Artifact artifact)
    {
        // Arrange: create parent User and World to satisfy FK chain
        var user = CreateValidUser();
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var worldEntity = CreateValidWorld(user.Id);
        Context.Worlds.Add(worldEntity);
        await Context.SaveChangesAsync();

        artifact.Id = Guid.NewGuid();
        artifact.WorldId = worldEntity.Id;
        artifact.RowVersion = [];

        var repo = new ArtifactRepository(Context);
        await repo.CreateAsync(artifact);

        // Act: mutate summary
        var newSummary = "Updated summary for " + artifact.Name;
        if (newSummary.Length > 2000)
            newSummary = newSummary[..2000];
        artifact.Summary = newSummary;
        await repo.UpdateAsync(artifact);

        // Assert
        using var freshContext = CreateNewContext();
        var retrieved = await freshContext.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artifact.Id);

        return retrieved is not null && retrieved.Summary == newSummary;
    }

    [FsCheck.NUnit.Property(Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) }, MaxTest = 100)]
    public async Task<bool> ArtifactFact_Update_Value_Is_Persisted(ArtifactFact fact)
    {
        // Arrange: create parent User, World, and Artifact to satisfy FK chain
        var user = CreateValidUser();
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var worldEntity = CreateValidWorld(user.Id);
        Context.Worlds.Add(worldEntity);
        await Context.SaveChangesAsync();

        var artifactEntity = CreateValidArtifact(worldEntity.Id);
        Context.Artifacts.Add(artifactEntity);
        await Context.SaveChangesAsync();

        fact.Id = Guid.NewGuid();
        fact.ArtifactId = artifactEntity.Id;
        fact.RowVersion = [];

        var repo = new ArtifactFactRepository(Context);
        await repo.CreateAsync(fact);

        // Act: mutate value
        var newValue = "Updated value: " + fact.Value;
        if (newValue.Length > 2000)
            newValue = newValue[..2000];
        fact.Value = newValue;
        await repo.UpdateAsync(fact);

        // Assert
        using var freshContext = CreateNewContext();
        var retrieved = await freshContext.ArtifactFacts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fact.Id);

        return retrieved is not null && retrieved.Value == newValue;
    }

    [FsCheck.NUnit.Property(Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) }, MaxTest = 100)]
    public async Task<bool> ArtifactRelationship_Update_Description_Is_Persisted(ArtifactRelationship relationship)
    {
        // Arrange: create parent User, World, and two Artifacts to satisfy FK chain
        var user = CreateValidUser();
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var worldEntity = CreateValidWorld(user.Id);
        Context.Worlds.Add(worldEntity);
        await Context.SaveChangesAsync();

        var artifactA = CreateValidArtifact(worldEntity.Id);
        var artifactB = CreateValidArtifact(worldEntity.Id);
        Context.Artifacts.AddRange(artifactA, artifactB);
        await Context.SaveChangesAsync();

        relationship.Id = Guid.NewGuid();
        relationship.WorldId = worldEntity.Id;
        relationship.ArtifactAId = artifactA.Id;
        relationship.ArtifactBId = artifactB.Id;
        relationship.RowVersion = [];

        var repo = new ArtifactRelationshipRepository(Context);
        await repo.CreateAsync(relationship);

        // Act: mutate description
        var newDescription = "Updated: " + (relationship.Description ?? "no description");
        if (newDescription.Length > 2000)
            newDescription = newDescription[..2000];
        relationship.Description = newDescription;
        await repo.UpdateAsync(relationship);

        // Assert
        using var freshContext = CreateNewContext();
        var retrieved = await freshContext.ArtifactRelationships
            .AsNoTracking()
            .FirstOrDefaultAsync(ar => ar.Id == relationship.Id);

        return retrieved is not null && retrieved.Description == newDescription;
    }

    [FsCheck.NUnit.Property(Arbitrary = new[] { typeof(EntityGenerators.DomainArbitraries) }, MaxTest = 100)]
    public async Task<bool> ReviewProposal_Update_Status_Is_Persisted(ReviewProposal proposal)
    {
        // Arrange: create parent User, World, Source, and ReviewBatch to satisfy FK chain
        var user = CreateValidUser();
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var worldEntity = CreateValidWorld(user.Id);
        Context.Worlds.Add(worldEntity);
        await Context.SaveChangesAsync();

        var source = CreateValidSource(worldEntity.Id, user.Id);
        Context.Sources.Add(source);
        await Context.SaveChangesAsync();

        var reviewBatch = CreateValidReviewBatch(worldEntity.Id, source.Id);
        Context.ReviewBatches.Add(reviewBatch);
        await Context.SaveChangesAsync();

        proposal.Id = Guid.NewGuid();
        proposal.ReviewBatchId = reviewBatch.Id;
        proposal.ReviewedByUserId = null;
        proposal.RowVersion = [];

        var repo = new ReviewProposalRepository(Context);
        await repo.CreateAsync(proposal);

        // Act: mutate status from whatever it is to Accepted
        var newStatus = proposal.Status == ReviewProposalStatus.Accepted
            ? ReviewProposalStatus.Rejected
            : ReviewProposalStatus.Accepted;
        proposal.Status = newStatus;
        await repo.UpdateAsync(proposal);

        // Assert
        using var freshContext = CreateNewContext();
        var retrieved = await freshContext.ReviewProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(rp => rp.Id == proposal.Id);

        return retrieved is not null && retrieved.Status == newStatus;
    }

    #region Helper methods for creating parent entities

    private static User CreateValidUser() => new()
    {
        Id = Guid.NewGuid(),
        Auth0SubjectId = Guid.NewGuid().ToString(),
        Username = "TestUser",
        Email = "test@example.com",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = []
    };

    private static World CreateValidWorld(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Black Harbor Investigation",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = userId,
        RowVersion = []
    };

    private static Artifact CreateValidArtifact(Guid worldId) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        Type = ArtifactType.Character,
        Name = "Captain Voss",
        Visibility = VisibilityScope.PartyVisible,
        Status = ArtifactStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = []
    };

    private static Source CreateValidSource(Guid worldId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        Type = SourceType.SessionNote,
        Title = "Session 1 Notes",
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = userId,
        Visibility = VisibilityScope.PartyVisible,
        ProcessingStatus = SourceProcessingStatus.Processed
    };

    private static ReviewBatch CreateValidReviewBatch(Guid worldId, Guid sourceId) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId,
        SourceId = sourceId,
        Status = ReviewBatchStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow
    };

    #endregion
}
