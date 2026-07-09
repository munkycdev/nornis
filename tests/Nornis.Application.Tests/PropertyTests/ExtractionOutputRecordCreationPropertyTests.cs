using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.PropertyTests;

/// <summary>
/// Property 13: Extraction Output Record Creation
///
/// For any successful AI response containing N proposals (N ≥ 1), the ExtractionService
/// SHALL create exactly one ReviewBatch (with WorldId, SourceId, Status=Pending,
/// CreatedAt ≈ now), exactly N ReviewProposal records (each with correct ChangeType,
/// TargetType, TargetId, ProposedValueJson ≤ 50,000 chars, Rationale, Confidence between
/// 0.00–1.00, Status=Pending), and exactly N SourceReference records (each with
/// TargetType=ReviewProposal, TargetId=the proposal's Id, and SourceId=the extraction
/// source's Id).
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4**
/// </summary>
[TestFixture]
[Category("Feature: async-source-extraction, Property 13: Extraction Output Record Creation")]
public class ExtractionOutputRecordCreationPropertyTests
{
    private static (ExtractionService Service, InMemorySourceRepository SourceRepo,
        InMemoryReviewBatchRepository BatchRepo, InMemoryReviewProposalRepository ProposalRepo,
        InMemorySourceReferenceRepository RefRepo, FakeAiExtractionClient AiClient) CreateServiceFixture()
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
            MaxArtifactContextCount = 50,
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

        return (service, sourceRepo, reviewBatchRepo, reviewProposalRepo, sourceReferenceRepo, fakeAiClient);
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 13: Extraction Output Record Creation")]
    public void Successful_extraction_creates_exactly_one_review_batch_with_correct_fields(
        Source source,
        AiExtractionResponse aiResponse)
    {
        // Arrange
        var (service, sourceRepo, batchRepo, _, _, aiClient) = CreateServiceFixture();
        sourceRepo.Seed(source);
        aiClient.SetupSuccess(aiResponse);

        var beforeUtc = DateTimeOffset.UtcNow;

        // Act
        var outcome = service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        var afterUtc = DateTimeOffset.UtcNow;

        // Assert — exactly 1 ReviewBatch
        Assert.That(batchRepo.Batches.Count, Is.EqualTo(1),
            "Exactly one ReviewBatch should be created for a successful extraction.");

        var batch = batchRepo.Batches[0];
        Assert.That(batch.WorldId, Is.EqualTo(source.WorldId),
            "ReviewBatch.WorldId should match the source's WorldId.");
        Assert.That(batch.SourceId, Is.EqualTo(source.Id),
            "ReviewBatch.SourceId should match the source's Id.");
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending),
            "ReviewBatch.Status should be Pending.");
        Assert.That(batch.CreatedAt, Is.GreaterThanOrEqualTo(beforeUtc).And.LessThanOrEqualTo(afterUtc),
            "ReviewBatch.CreatedAt should be approximately now.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 13: Extraction Output Record Creation")]
    public void Successful_extraction_creates_exactly_N_review_proposals_with_correct_fields(
        Source source,
        AiExtractionResponse aiResponse)
    {
        // Arrange
        var (service, sourceRepo, batchRepo, proposalRepo, _, aiClient) = CreateServiceFixture();
        sourceRepo.Seed(source);
        aiClient.SetupSuccess(aiResponse);

        var expectedCount = aiResponse.Proposals.Count;

        // Act
        service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert — exactly N ReviewProposals
        Assert.That(proposalRepo.Proposals.Count, Is.EqualTo(expectedCount),
            $"Should create exactly {expectedCount} ReviewProposal records (one per AI proposal).");

        var batch = batchRepo.Batches[0];

        for (var i = 0; i < expectedCount; i++)
        {
            var aiProposal = aiResponse.Proposals[i];
            var proposal = proposalRepo.Proposals[i];

            Assert.That(proposal.ReviewBatchId, Is.EqualTo(batch.Id),
                $"Proposal[{i}].ReviewBatchId should reference the created batch.");
            Assert.That(proposal.ChangeType.ToString(), Is.EqualTo(aiProposal.ChangeType),
                $"Proposal[{i}].ChangeType should match the AI proposal's ChangeType.");
            Assert.That(proposal.TargetType.ToString(), Is.EqualTo(aiProposal.TargetType),
                $"Proposal[{i}].TargetType should match the AI proposal's TargetType.");
            Assert.That(proposal.TargetId, Is.EqualTo(aiProposal.TargetId),
                $"Proposal[{i}].TargetId should match the AI proposal's TargetId.");
            Assert.That(proposal.ProposedValueJson.Length, Is.LessThanOrEqualTo(50_000),
                $"Proposal[{i}].ProposedValueJson should be ≤ 50,000 characters.");
            Assert.That(proposal.Rationale, Is.EqualTo(aiProposal.Rationale),
                $"Proposal[{i}].Rationale should match the AI proposal's Rationale.");

            if (aiProposal.Confidence is not null)
            {
                Assert.That(proposal.Confidence, Is.GreaterThanOrEqualTo(0.00m).And.LessThanOrEqualTo(1.00m),
                    $"Proposal[{i}].Confidence should be between 0.00 and 1.00.");
            }

            Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending),
                $"Proposal[{i}].Status should be Pending.");
        }
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ExtractionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: async-source-extraction, Property 13: Extraction Output Record Creation")]
    public void Successful_extraction_creates_exactly_N_source_references_with_correct_fields(
        Source source,
        AiExtractionResponse aiResponse)
    {
        // Arrange
        var (service, sourceRepo, _, proposalRepo, refRepo, aiClient) = CreateServiceFixture();
        sourceRepo.Seed(source);
        aiClient.SetupSuccess(aiResponse);

        var expectedCount = aiResponse.Proposals.Count;

        // Act
        service.ProcessExtractionAsync(source.Id, source.WorldId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert — exactly N SourceReferences
        Assert.That(refRepo.References.Count, Is.EqualTo(expectedCount),
            $"Should create exactly {expectedCount} SourceReference records (one per proposal).");

        for (var i = 0; i < expectedCount; i++)
        {
            var proposal = proposalRepo.Proposals[i];
            var sourceRef = refRepo.References[i];

            Assert.That(sourceRef.TargetType, Is.EqualTo(SourceReferenceTargetType.ReviewProposal),
                $"SourceReference[{i}].TargetType should be ReviewProposal.");
            Assert.That(sourceRef.TargetId, Is.EqualTo(proposal.Id),
                $"SourceReference[{i}].TargetId should reference the proposal's Id.");
            Assert.That(sourceRef.SourceId, Is.EqualTo(source.Id),
                $"SourceReference[{i}].SourceId should reference the extraction source's Id.");
        }
    }
}
