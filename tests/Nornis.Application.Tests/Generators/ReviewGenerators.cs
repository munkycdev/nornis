using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Generators;

/// <summary>
/// FsCheck generators for review proposal workflow property tests.
/// </summary>
public static class ReviewGenerators
{
    private static readonly string[] ArtifactNames =
    [
        "Captain Voss", "Tavrin", "Black Harbor", "Silver Key",
        "Missing Caravan", "The Red Lodge", "Iron Gate", "Whisperwind",
        "Shadowmere", "Elara", "Duskport", "Stormveil Keep",
        "The Obsidian Dagger", "Frostfell", "Moonbridge"
    ];

    private static readonly string[] Predicates =
    [
        "location", "occupation", "allegiance", "status", "known_associate",
        "last_seen", "motivation", "weakness", "artifact_origin", "rumor",
        "confirmed_trait", "connection", "possession", "threat_level"
    ];

    private static readonly string[] FactValues =
    [
        "Black Harbor", "Harbor Master", "The Red Lodge", "Missing",
        "Tavrin spotted them near the docks", "Allied with Silver Key faction",
        "Last seen heading north toward the mountains",
        "Controls the Black Harbor trade routes",
        "Suspected of involvement in the missing caravan incident"
    ];

    private static readonly string[] RelationshipTypes =
    [
        "LocatedIn", "AlliedWith", "SuspectedIn", "CreatedBy",
        "OpposedTo", "EmployedBy", "ConnectedTo", "MemberOf",
        "FoundIn", "TraveledTo"
    ];

    private static readonly string[] TruthStates =
    [
        "Confirmed", "Likely", "Rumor", "Disputed", "False", "Hidden"
    ];

    private static readonly string[] VisibilityValues =
    [
        "Private", "GMOnly", "PartyVisible"
    ];

    // --- Enum generators ---

    public static Gen<WorldRole> WorldRoleGen =>
        Gen.Elements(WorldRole.GM, WorldRole.Player, WorldRole.Observer);

    public static Gen<VisibilityScope> VisibilityScopeGen =>
        Gen.Elements(VisibilityScope.Private, VisibilityScope.GMOnly, VisibilityScope.PartyVisible);

    public static Gen<ReviewProposalStatus> ReviewProposalStatusGen =>
        Gen.Elements(
            ReviewProposalStatus.Pending,
            ReviewProposalStatus.Accepted,
            ReviewProposalStatus.Rejected,
            ReviewProposalStatus.Edited);

    public static Gen<ReviewChangeType> ReviewChangeTypeGen =>
        Gen.Elements(
            ReviewChangeType.CreateArtifact,
            ReviewChangeType.UpdateArtifact,
            ReviewChangeType.MergeArtifact,
            ReviewChangeType.AddFact,
            ReviewChangeType.UpdateFact,
            ReviewChangeType.AddRelationship,
            ReviewChangeType.UpdateRelationship);

    public static Gen<ArtifactType> ArtifactTypeGen =>
        Gen.Elements(Enum.GetValues<ArtifactType>());

    // --- Payload generators ---

    /// <summary>
    /// Generates valid CreateArtifact JSON payloads with Name (1-200), valid ArtifactType,
    /// optional Summary, optional Visibility, optional Confidence (0-1).
    /// </summary>
    public static Gen<string> ValidCreateArtifactPayload =>
        from name in Gen.Elements(ArtifactNames)
        from artifactType in ArtifactTypeGen
        from hasSummary in ArbMap.Default.GeneratorFor<bool>()
        from hasVisibility in ArbMap.Default.GeneratorFor<bool>()
        from hasConfidence in ArbMap.Default.GeneratorFor<bool>()
        from confidence in Gen.Choose(0, 100)
        from visibility in Gen.Elements(VisibilityValues)
        let payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["type"] = artifactType.ToString(),
            ["summary"] = hasSummary ? $"Summary of {name} in the world" : null,
            ["visibility"] = hasVisibility ? visibility : null,
            ["confidence"] = hasConfidence ? (decimal)confidence / 100m : null
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)
        select JsonSerializer.Serialize(payload, JsonSerializerOptions);

    /// <summary>
    /// Generates valid AddFact JSON payloads with Predicate (1-500), Value (1-4000),
    /// optional Confidence, TruthState, Visibility.
    /// </summary>
    public static Gen<string> ValidAddFactPayload =>
        from predicate in Gen.Elements(Predicates)
        from value in Gen.Elements(FactValues)
        from hasConfidence in ArbMap.Default.GeneratorFor<bool>()
        from confidence in Gen.Choose(0, 100)
        from hasTruthState in ArbMap.Default.GeneratorFor<bool>()
        from truthState in Gen.Elements(TruthStates)
        from hasVisibility in ArbMap.Default.GeneratorFor<bool>()
        from visibility in Gen.Elements(VisibilityValues)
        let payload = new Dictionary<string, object?>
        {
            ["predicate"] = predicate,
            ["value"] = value,
            ["confidence"] = hasConfidence ? (decimal)confidence / 100m : null,
            ["truthState"] = hasTruthState ? truthState : null,
            ["visibility"] = hasVisibility ? visibility : null
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)
        select JsonSerializer.Serialize(payload, JsonSerializerOptions);

    /// <summary>
    /// Generates valid AddRelationship JSON payloads with ArtifactAId, ArtifactBId (non-empty GUIDs),
    /// Type (1-200), optional Description, Confidence, TruthState, Visibility.
    /// </summary>
    public static Gen<string> ValidAddRelationshipPayload =>
        from artifactAId in ArbMap.Default.GeneratorFor<Guid>()
        where artifactAId != Guid.Empty
        from artifactBId in ArbMap.Default.GeneratorFor<Guid>()
        where artifactBId != Guid.Empty && artifactBId != artifactAId
        from type in Gen.Elements(RelationshipTypes)
        from hasDescription in ArbMap.Default.GeneratorFor<bool>()
        from hasConfidence in ArbMap.Default.GeneratorFor<bool>()
        from confidence in Gen.Choose(0, 100)
        from hasTruthState in ArbMap.Default.GeneratorFor<bool>()
        from truthState in Gen.Elements(TruthStates)
        from hasVisibility in ArbMap.Default.GeneratorFor<bool>()
        from visibility in Gen.Elements(VisibilityValues)
        let payload = new Dictionary<string, object?>
        {
            ["artifactAId"] = artifactAId.ToString(),
            ["artifactBId"] = artifactBId.ToString(),
            ["type"] = type,
            ["description"] = hasDescription ? $"{type} relationship description" : null,
            ["confidence"] = hasConfidence ? (decimal)confidence / 100m : null,
            ["truthState"] = hasTruthState ? truthState : null,
            ["visibility"] = hasVisibility ? visibility : null
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)
        select JsonSerializer.Serialize(payload, JsonSerializerOptions);

    /// <summary>
    /// Generates invalid ProposedValueJson: malformed JSON, missing required fields, oversized.
    /// </summary>
    public static Gen<string> InvalidProposedValueJson =>
        Gen.OneOf(
            // Malformed JSON
            Gen.Elements(
                "{not valid json",
                "{ name: missing quotes }",
                "",
                "null",
                "[]"),
            // JSON exceeding 32768 chars
            Gen.Constant(new string('x', 32769)),
            // Missing required fields for CreateArtifact
            Gen.Constant(JsonSerializer.Serialize(new { type = "Character" }, JsonSerializerOptions)),
            // Missing required fields for AddFact
            Gen.Constant(JsonSerializer.Serialize(new { predicate = "location" }, JsonSerializerOptions)),
            // Empty object
            Gen.Constant("{}")
        );

    // --- Scenario generators ---

    /// <summary>
    /// Generates a complete ReviewScenario with a world, multiple sources with mixed visibility,
    /// batches, proposals, and world members with different roles.
    /// This is the most complex generator — it creates a realistic test world.
    /// </summary>
    public static Gen<ReviewScenario> ReviewScenarioGen =>
        from sourceCount in Gen.Choose(1, 4)
        from proposalsPerBatch in Gen.Choose(1, 5)
        from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
        from playerUserId in ArbMap.Default.GeneratorFor<Guid>()
        where playerUserId != gmUserId
        from observerUserId in ArbMap.Default.GeneratorFor<Guid>()
        where observerUserId != gmUserId && observerUserId != playerUserId
        from visibilities in VisibilityScopeGen.ArrayOf(sourceCount)
        from sourceOwners in Gen.Elements(true, false).ArrayOf(sourceCount)
        let worldId = Guid.NewGuid()
        let sources = Enumerable.Range(0, sourceCount).Select(i => new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = $"Source {i + 1} — {ArtifactNames[i % ArtifactNames.Length]}",
            Body = $"Content about {ArtifactNames[i % ArtifactNames.Length]}",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-(sourceCount - i)),
            CreatedByUserId = sourceOwners[i] ? playerUserId : gmUserId,
            Visibility = visibilities[i],
            ProcessingStatus = SourceProcessingStatus.Processed
        }).ToList()
        let batches = sources.Select(s => new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = s.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = s.CreatedAt.AddMinutes(5)
        }).ToList()
        let proposals = batches.SelectMany((b, bi) =>
            Enumerable.Range(0, proposalsPerBatch).Select(pi => new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = b.Id,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                TargetId = null,
                ProposedValueJson = JsonSerializer.Serialize(new
                {
                    name = $"{ArtifactNames[(bi * proposalsPerBatch + pi) % ArtifactNames.Length]}",
                    type = "Character",
                    summary = "A notable figure",
                    visibility = "PartyVisible",
                    confidence = 0.85m
                }, JsonSerializerOptions),
                Rationale = "Extracted from source content",
                Confidence = 0.85m,
                Status = ReviewProposalStatus.Pending,
                CreatedAt = b.CreatedAt.AddMinutes(pi + 1)
            })).ToList()
        let members = new List<WorldMember>
        {
            new()
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = gmUserId,
                Role = WorldRole.GM,
                DisplayName = "Kelda",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-30)
            },
            new()
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = playerUserId,
                Role = WorldRole.Player,
                DisplayName = "Tavrin",
                CharacterName = "Tavrin the Bold",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-28)
            },
            new()
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = observerUserId,
                Role = WorldRole.Observer,
                DisplayName = "Jorin",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-25)
            }
        }
        select new ReviewScenario(
            worldId,
            sources,
            batches,
            proposals,
            members,
            gmUserId,
            playerUserId,
            observerUserId);

    /// <summary>
    /// Generates a CreateArtifact proposal with valid JSON that can be directly accepted.
    /// Includes world/source/batch context needed by ReviewService.
    /// </summary>
    public static Gen<ProposalWithContext> CreateArtifactProposalWithContext =>
        from payload in ValidCreateArtifactPayload
        from visibility in VisibilityScopeGen
        from userId in ArbMap.Default.GeneratorFor<Guid>()
        let worldId = Guid.NewGuid()
        let source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Test content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed
        }
        let batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        }
        let proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = payload,
            Rationale = "Extracted from source",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        }
        select new ProposalWithContext(proposal, batch, source, worldId, userId);

    /// <summary>
    /// Generates an AddFact proposal with valid JSON and an existing target artifact.
    /// </summary>
    public static Gen<ProposalWithContext> AddFactProposalWithContext =>
        from payload in ValidAddFactPayload
        from visibility in VisibilityScopeGen
        from userId in ArbMap.Default.GeneratorFor<Guid>()
        let worldId = Guid.NewGuid()
        let artifactId = Guid.NewGuid()
        let source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Test content",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed
        }
        let batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        }
        let proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.AddFact,
            TargetType = ReviewTargetType.ArtifactFact,
            TargetId = artifactId,
            ProposedValueJson = payload,
            Rationale = "Extracted fact from source",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        }
        select new ProposalWithContext(proposal, batch, source, worldId, userId, artifactId);

    /// <summary>
    /// Generates a proposal with Pending or Edited status suitable for accept/reject/edit operations.
    /// </summary>
    public static Gen<ProposalWithContext> ReviewableProposalWithContext =>
        from status in Gen.Elements(ReviewProposalStatus.Pending, ReviewProposalStatus.Edited)
        from payload in ValidCreateArtifactPayload
        from visibility in VisibilityScopeGen
        from userId in ArbMap.Default.GeneratorFor<Guid>()
        let worldId = Guid.NewGuid()
        let source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content about Captain Voss",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed
        }
        let batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        }
        let proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = null,
            ProposedValueJson = payload,
            Rationale = "Extracted from source",
            Confidence = 0.85m,
            Status = status,
            CreatedAt = batch.CreatedAt.AddMinutes(1)
        }
        select new ProposalWithContext(proposal, batch, source, worldId, userId);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

/// <summary>
/// A complete test world for review property tests: world, sources (with mixed visibility),
/// batches, proposals, and members with varying roles.
/// </summary>
public record ReviewScenario(
    Guid WorldId,
    List<Source> Sources,
    List<ReviewBatch> Batches,
    List<ReviewProposal> Proposals,
    List<WorldMember> Members,
    Guid GmUserId,
    Guid PlayerUserId,
    Guid ObserverUserId);

/// <summary>
/// A single proposal with its full context (source, batch, world) for property tests.
/// </summary>
public record ProposalWithContext(
    ReviewProposal Proposal,
    ReviewBatch Batch,
    Source Source,
    Guid WorldId,
    Guid OwnerUserId,
    Guid? TargetArtifactId = null);

/// <summary>
/// FsCheck Arbitrary registrations for review workflow property tests.
/// Use as [Property(Arbitrary = [typeof(ReviewArbitraries)])]
/// </summary>
public class ReviewArbitraries
{
    public static Arbitrary<ReviewScenario> ReviewScenarios() =>
        ReviewGenerators.ReviewScenarioGen.ToArbitrary();

    public static Arbitrary<ProposalWithContext> ProposalsWithContext() =>
        ReviewGenerators.CreateArtifactProposalWithContext.ToArbitrary();

    public static Arbitrary<WorldRole> WorldRoles() =>
        ReviewGenerators.WorldRoleGen.ToArbitrary();

    public static Arbitrary<VisibilityScope> VisibilityScopes() =>
        ReviewGenerators.VisibilityScopeGen.ToArbitrary();

    public static Arbitrary<ReviewProposalStatus> ReviewProposalStatuses() =>
        ReviewGenerators.ReviewProposalStatusGen.ToArbitrary();

    public static Arbitrary<ReviewChangeType> ReviewChangeTypes() =>
        ReviewGenerators.ReviewChangeTypeGen.ToArbitrary();
}

