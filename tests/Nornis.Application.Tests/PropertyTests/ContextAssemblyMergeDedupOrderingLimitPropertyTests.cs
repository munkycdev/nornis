using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.PropertyTests;

/// <summary>
/// Property 7: Context Assembly Merge, Dedup, Ordering, and Limit
///
/// For any campaign with N artifacts (where N exceeds the configured MaxArtifactContextCount),
/// the assembled context SHALL contain at most MaxArtifactContextCount artifacts, with
/// name-matched artifacts appearing before recently-updated artifacts, and no artifact
/// appearing more than once in the list.
///
/// **Validates: Requirements 4.1, 4.2, 4.3**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 7: Context Assembly Merge, Dedup, Ordering, and Limit")]
public class ContextAssemblyMergeDedupOrderingLimitPropertyTests
{
    private const int MaxArtifactContextCount = 10;

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 7: Context Assembly Merge, Dedup, Ordering, and Limit")]
    public Property Context_assembly_respects_max_count_limit()
    {
        return Prop.ForAll(
            ScenarioGen().ToArbitrary(),
            ExtractionGenerators.ValidExtractionResponse.ToArbitrary(),
            (ContextAssemblyScenario scenario, AiExtractionResponse aiResponse) =>
            {
                var (service, fakeAiClient) = CreateServiceWithScenario(scenario, aiResponse);

                service.ProcessExtractionAsync(scenario.Source.Id, scenario.Source.CampaignId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var request = fakeAiClient.Requests.FirstOrDefault();

                return (request is not null)
                    .Label("AI should have been called")
                    .And((request!.ExistingArtifacts.Count <= MaxArtifactContextCount)
                        .Label($"Context should be at most {MaxArtifactContextCount} but was {request.ExistingArtifacts.Count}"));
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 7: Context Assembly Merge, Dedup, Ordering, and Limit")]
    public Property Context_assembly_has_no_duplicate_artifacts()
    {
        return Prop.ForAll(
            ScenarioGen().ToArbitrary(),
            ExtractionGenerators.ValidExtractionResponse.ToArbitrary(),
            (ContextAssemblyScenario scenario, AiExtractionResponse aiResponse) =>
            {
                var (service, fakeAiClient) = CreateServiceWithScenario(scenario, aiResponse);

                service.ProcessExtractionAsync(scenario.Source.Id, scenario.Source.CampaignId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var request = fakeAiClient.Requests.FirstOrDefault();
                if (request is null)
                    return false.Label("AI should have been called");

                var ids = request.ExistingArtifacts.Select(a => a.Id).ToList();
                var distinctIds = ids.Distinct().ToList();

                return (ids.Count == distinctIds.Count)
                    .Label($"No duplicates expected. Got {ids.Count} items with {distinctIds.Count} distinct.");
            });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 7: Context Assembly Merge, Dedup, Ordering, and Limit")]
    public Property Context_assembly_places_name_matched_before_recent_only()
    {
        return Prop.ForAll(
            ScenarioGen().ToArbitrary(),
            ExtractionGenerators.ValidExtractionResponse.ToArbitrary(),
            (ContextAssemblyScenario scenario, AiExtractionResponse aiResponse) =>
            {
                var (service, fakeAiClient) = CreateServiceWithScenario(scenario, aiResponse);

                service.ProcessExtractionAsync(scenario.Source.Id, scenario.Source.CampaignId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var request = fakeAiClient.Requests.FirstOrDefault();
                if (request is null)
                    return false.Label("AI should have been called");

                var contextIds = request.ExistingArtifacts.Select(a => a.Id).ToList();

                // Find the last index of any name-matched artifact in the context
                var lastNameMatchedIndex = -1;
                for (var i = 0; i < contextIds.Count; i++)
                {
                    if (scenario.NameMatchedArtifactIds.Contains(contextIds[i]))
                    {
                        lastNameMatchedIndex = i;
                    }
                }

                // Find the first index of any recent-only artifact in the context
                var firstRecentOnlyIndex = int.MaxValue;
                for (var i = 0; i < contextIds.Count; i++)
                {
                    if (scenario.RecentOnlyArtifactIds.Contains(contextIds[i]))
                    {
                        firstRecentOnlyIndex = i;
                        break;
                    }
                }

                // If there are no recent-only artifacts in context, the property trivially holds
                if (firstRecentOnlyIndex == int.MaxValue)
                    return true.Label("No recent-only artifacts in context (trivially satisfied)");

                // If there are no name-matched artifacts in context, the property trivially holds
                if (lastNameMatchedIndex == -1)
                    return true.Label("No name-matched artifacts in context (trivially satisfied)");

                return (lastNameMatchedIndex < firstRecentOnlyIndex)
                    .Label($"Last name-matched index ({lastNameMatchedIndex}) should be before first recent-only index ({firstRecentOnlyIndex})");
            });
    }

    /// <summary>
    /// Generates a scenario where total artifacts exceed MaxArtifactContextCount.
    /// Artifacts are divided into three groups:
    /// - Name-matched only (names appear in source body, but old UpdatedAt)
    /// - Overlap (names appear in source body AND recently updated)
    /// - Recent-only (names do NOT appear in source body, recently updated)
    /// </summary>
    private static Gen<ContextAssemblyScenario> ScenarioGen()
    {
        return from nameMatchedCount in Gen.Choose(2, 6)
               from recentOnlyCount in Gen.Choose(2, 6)
               from overlapCount in Gen.Choose(1, 4)
               from visibility in Gen.Elements(Enum.GetValues<VisibilityScope>())
               let campaignId = Guid.NewGuid()
               let total = nameMatchedCount + recentOnlyCount + overlapCount
               where total > MaxArtifactContextCount
               let nameMatchedArtifacts = Enumerable.Range(0, nameMatchedCount).Select(i =>
                   CreateArtifact(campaignId, $"NameOnly{i}", visibility,
                       DateTimeOffset.UtcNow.AddDays(-(100 + i)))).ToList()
               let overlapArtifacts = Enumerable.Range(0, overlapCount).Select(i =>
                   CreateArtifact(campaignId, $"Overlap{i}", visibility,
                       DateTimeOffset.UtcNow.AddDays(-i))).ToList()
               let recentOnlyArtifacts = Enumerable.Range(0, recentOnlyCount).Select(i =>
                   CreateArtifact(campaignId, $"RecentOnly{i}", visibility,
                       DateTimeOffset.UtcNow.AddDays(-(i + overlapCount)))).ToList()
               let allArtifacts = nameMatchedArtifacts
                   .Concat(overlapArtifacts)
                   .Concat(recentOnlyArtifacts).ToList()
               let nameMatchedNames = nameMatchedArtifacts.Select(a => a.Name)
                   .Concat(overlapArtifacts.Select(a => a.Name))
               let sourceBody = $"We met {string.Join(" and ", nameMatchedNames)} in the tavern."
               let source = new Source
               {
                   Id = Guid.NewGuid(),
                   CampaignId = campaignId,
                   Type = SourceType.SessionNote,
                   Title = "Context test session",
                   Body = sourceBody,
                   CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                   CreatedByUserId = Guid.NewGuid(),
                   Visibility = visibility,
                   ProcessingStatus = SourceProcessingStatus.Queued
               }
               select new ContextAssemblyScenario
               {
                   Source = source,
                   AllArtifacts = allArtifacts,
                   NameMatchedArtifactIds = nameMatchedArtifacts.Select(a => a.Id)
                       .Concat(overlapArtifacts.Select(a => a.Id)).ToHashSet(),
                   RecentOnlyArtifactIds = recentOnlyArtifacts.Select(a => a.Id).ToHashSet(),
                   OverlapArtifactIds = overlapArtifacts.Select(a => a.Id).ToHashSet(),
                   TotalArtifactCount = total
               };
    }

    private static Artifact CreateArtifact(Guid campaignId, string name, VisibilityScope visibility, DateTimeOffset updatedAt)
    {
        return new Artifact
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            Type = ArtifactType.Character,
            Name = name,
            Summary = $"Summary of {name}",
            Visibility = visibility,
            Confidence = 0.8m,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = updatedAt
        };
    }

    private (ExtractionService Service, FakeAiExtractionClient FakeAi) CreateServiceWithScenario(
        ContextAssemblyScenario scenario,
        AiExtractionResponse aiResponse)
    {
        var sourceRepo = new InMemorySourceRepository();
        var reviewBatchRepo = new InMemoryReviewBatchRepository();
        var reviewProposalRepo = new InMemoryReviewProposalRepository();
        var sourceReferenceRepo = new InMemorySourceReferenceRepository();
        var aiUsageRecordRepo = new InMemoryAiUsageRecordRepository();
        var artifactRepo = new InMemoryArtifactRepository();
        var artifactFactRepo = new InMemoryArtifactFactRepository();
        var fakeAiClient = new FakeAiExtractionClient();
        var unitOfWork = new FakeUnitOfWork();

        var options = Options.Create(new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = MaxArtifactContextCount,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                }
            }
        });

        var logger = NullLogger<ExtractionService>.Instance;

        var service = new ExtractionService(
            sourceRepo,
            reviewBatchRepo,
            reviewProposalRepo,
            sourceReferenceRepo,
            aiUsageRecordRepo,
            artifactRepo,
            artifactFactRepo,
            fakeAiClient,
            new FakeAiBudgetGuard(), unitOfWork,
            options,
            logger);

        sourceRepo.Seed(scenario.Source);
        artifactRepo.Seed(scenario.AllArtifacts);
        fakeAiClient.SetupSuccess(aiResponse);

        return (service, fakeAiClient);
    }
}

/// <summary>
/// Scenario data for context assembly property tests.
/// </summary>
public class ContextAssemblyScenario
{
    public required Source Source { get; init; }
    public required List<Artifact> AllArtifacts { get; init; }
    public required HashSet<Guid> NameMatchedArtifactIds { get; init; }
    public required HashSet<Guid> RecentOnlyArtifactIds { get; init; }
    public required HashSet<Guid> OverlapArtifactIds { get; init; }
    public required int TotalArtifactCount { get; init; }
}
