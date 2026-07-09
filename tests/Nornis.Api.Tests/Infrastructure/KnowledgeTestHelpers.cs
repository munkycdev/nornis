using Microsoft.Extensions.DependencyInjection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Helper methods for seeding artifacts, facts, relationships, and source references directly
/// into the in-memory database. Artifacts are normally created by accepting review proposals;
/// these helpers set up read-side preconditions without driving the whole extraction pipeline.
/// </summary>
public static class KnowledgeTestHelpers
{
    public static async Task<Artifact> CreateTestArtifactAsync(
        NornisWebApplicationFactory factory,
        Guid worldId,
        string name = "Captain Voss",
        ArtifactType type = ArtifactType.Character,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        ArtifactStatus status = ArtifactStatus.Active,
        string? summary = null,
        decimal? confidence = null,
        DateTimeOffset? updatedAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = type,
            Name = name,
            Summary = summary,
            Visibility = visibility,
            Status = status,
            Confidence = confidence,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now
        };

        db.Artifacts.Add(artifact);
        await db.SaveChangesAsync();
        return artifact;
    }

    public static async Task<ArtifactFact> CreateTestFactAsync(
        NornisWebApplicationFactory factory,
        Guid artifactId,
        string predicate = "location",
        string value = "Black Harbor",
        TruthState truthState = TruthState.Confirmed,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        decimal? confidence = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var now = DateTimeOffset.UtcNow;
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            Confidence = confidence,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ArtifactFacts.Add(fact);
        await db.SaveChangesAsync();
        return fact;
    }

    public static async Task<ArtifactRelationship> CreateTestRelationshipAsync(
        NornisWebApplicationFactory factory,
        Guid worldId,
        Guid artifactAId,
        Guid artifactBId,
        string type = "LocatedIn",
        TruthState truthState = TruthState.Confirmed,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        string? description = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var now = DateTimeOffset.UtcNow;
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            ArtifactAId = artifactAId,
            ArtifactBId = artifactBId,
            Type = type,
            Description = description,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ArtifactRelationships.Add(relationship);
        await db.SaveChangesAsync();
        return relationship;
    }

    public static async Task<SourceReference> CreateTestSourceReferenceAsync(
        NornisWebApplicationFactory factory,
        Guid sourceId,
        SourceReferenceTargetType targetType,
        Guid targetId,
        string? quote = null,
        string? notes = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var reference = new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            TargetType = targetType,
            TargetId = targetId,
            Quote = quote,
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.SourceReferences.Add(reference);
        await db.SaveChangesAsync();
        return reference;
    }
}
