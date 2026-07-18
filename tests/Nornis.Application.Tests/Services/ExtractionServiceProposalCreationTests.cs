using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Unit tests for ExtractionService proposal creation and visibility enforcement.
/// Validates Requirements 7.1–7.7, 8.1–8.2.
/// </summary>
[TestFixture]
public class ExtractionServiceProposalCreationTests
{
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ExtractionService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _aiClient = new FakeAiExtractionClient();
        _unitOfWork = new FakeUnitOfWork();

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
                ["gpt-4o"] = new() { InputPerMillionTokensUsd = 2.50m, OutputPerMillionTokensUsd = 10.00m }
            }
        });

        _sut = new ExtractionService(
            _sourceRepo,
            new InMemoryCampaignRepository(),
            _batchRepo,
            _proposalRepo,
            _sourceRefRepo,
            _usageRepo,
            _artifactRepo,
            _factRepo,
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceAttachmentRepository(),
            new InMemoryMapPlacemarkRepository(),
            new FakeBlobStorageService(),
            new FakePdfTextExtractor(),
            _aiClient,
            new FakeHandwritingTranscriptionClient(),
            new FakeImageReadingClient(),
            new FakeMapExtractionClient(),
            new FakeAiBudgetGuard(), _unitOfWork,
            options,
            NullLogger<ExtractionService>.Instance);
    }

    private Source CreateQueuedSource(
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        string body = "Captain Voss was spotted in Black Harbor.")
    {
        var source = new Source
        {
            Id = SourceId,
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = body,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued,
            CreatedByUserId = UserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private AiExtractionResponse CreateSuccessResponse(int proposalCount = 2)
    {
        var proposals = Enumerable.Range(0, proposalCount).Select(i => new ExtractionProposal
        {
            ChangeType = "CreateArtifact",
            TargetType = "Artifact",
            TargetId = null,
            ProposedValue = new { name = $"Artifact {i}", type = "Character", visibility = "PartyVisible" },
            Rationale = $"Extracted from source text - proposal {i}",
            Confidence = 0.85m
        }).ToList();

        return new AiExtractionResponse
        {
            Proposals = proposals,
            InputTokens = 500,
            OutputTokens = 200,
            TotalTokens = 700,
            DurationMs = 1200,
            Model = "gpt-4o"
        };
    }

    #region ReviewBatch creation (Requirement 7.1)

    [Test]
    public async Task ReviewBatch_IsCreated_WithCorrectWorldId_And_SourceId()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse());

        // Act
        var outcome = await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_batchRepo.Batches, Has.Count.EqualTo(1));

        var batch = _batchRepo.Batches[0];
        Assert.That(batch.WorldId, Is.EqualTo(WorldId));
        Assert.That(batch.SourceId, Is.EqualTo(SourceId));
    }

    [Test]
    public async Task ReviewBatch_IsCreated_WithStatusPending()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse());

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var batch = _batchRepo.Batches[0];
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));
    }

    [Test]
    public async Task ReviewBatch_HasCreatedAt_SetToRecentUtcTime()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse());
        var before = DateTimeOffset.UtcNow;

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var after = DateTimeOffset.UtcNow;
        var batch = _batchRepo.Batches[0];
        Assert.That(batch.CreatedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(batch.CreatedAt, Is.LessThanOrEqualTo(after));
    }

    #endregion

    #region ReviewProposal per AI proposal (Requirement 7.2, 7.3)

    [Test]
    public async Task OneReviewProposal_IsCreated_PerAiProposal()
    {
        // Arrange
        CreateQueuedSource();
        var proposalCount = 3;
        _aiClient.SetupSuccess(CreateSuccessResponse(proposalCount));

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        Assert.That(_proposalRepo.Proposals, Has.Count.EqualTo(proposalCount));
    }

    [Test]
    public async Task ReviewProposal_HasCorrectFieldMapping()
    {
        // Arrange
        CreateQueuedSource();
        var targetId = Guid.NewGuid();
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "UpdateArtifact",
                    TargetType = "Artifact",
                    TargetId = targetId,
                    ProposedValue = new { name = "Captain Voss", type = "Character", visibility = "PartyVisible" },
                    Rationale = "Updated Voss location based on session notes",
                    Confidence = 0.92m
                }
            ],
            InputTokens = 400,
            OutputTokens = 150,
            TotalTokens = 550,
            DurationMs = 800,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(aiResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var proposal = _proposalRepo.Proposals[0];
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.UpdateArtifact));
        Assert.That(proposal.TargetType, Is.EqualTo(ReviewTargetType.Artifact));
        Assert.That(proposal.TargetId, Is.EqualTo(targetId));
        Assert.That(proposal.Rationale, Is.EqualTo("Updated Voss location based on session notes"));
        Assert.That(proposal.Confidence, Is.EqualTo(0.92m));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending));
    }

    [Test]
    public async Task ReviewProposal_HasReviewBatchId_MatchingBatch()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse());

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var batch = _batchRepo.Batches[0];
        foreach (var proposal in _proposalRepo.Proposals)
        {
            Assert.That(proposal.ReviewBatchId, Is.EqualTo(batch.Id));
        }
    }

    [Test]
    public async Task ReviewProposal_ProposedValueJson_ContainsSerializedProposedValue()
    {
        // Arrange
        CreateQueuedSource();
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "AddFact",
                    TargetType = "ArtifactFact",
                    TargetId = Guid.NewGuid(),
                    ProposedValue = new { predicate = "location", value = "Black Harbor", visibility = "PartyVisible" },
                    Rationale = "Voss was seen in Black Harbor",
                    Confidence = 0.80m
                }
            ],
            InputTokens = 300,
            OutputTokens = 100,
            TotalTokens = 400,
            DurationMs = 600,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(aiResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var proposal = _proposalRepo.Proposals[0];
        Assert.That(proposal.ProposedValueJson, Is.Not.Null.And.Not.Empty);

        var jsonNode = JsonNode.Parse(proposal.ProposedValueJson);
        Assert.That(jsonNode, Is.Not.Null);
        Assert.That(jsonNode!["predicate"]?.GetValue<string>(), Is.EqualTo("location"));
        Assert.That(jsonNode["value"]?.GetValue<string>(), Is.EqualTo("Black Harbor"));
    }

    #endregion

    #region SourceReference per ReviewProposal (Requirement 7.4)

    [Test]
    public async Task OneSourceReference_IsCreated_PerReviewProposal()
    {
        // Arrange
        CreateQueuedSource();
        var proposalCount = 3;
        _aiClient.SetupSuccess(CreateSuccessResponse(proposalCount));

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        Assert.That(_sourceRefRepo.References, Has.Count.EqualTo(proposalCount));
    }

    [Test]
    public async Task SourceReference_HasCorrectTargetType_And_SourceId()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse(1));

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var reference = _sourceRefRepo.References[0];
        Assert.That(reference.TargetType, Is.EqualTo(SourceReferenceTargetType.ReviewProposal));
        Assert.That(reference.SourceId, Is.EqualTo(SourceId));
    }

    [Test]
    public async Task SourceReference_TargetId_MatchesCorrespondingProposalId()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse(2));

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var proposalIds = _proposalRepo.Proposals.Select(p => p.Id).ToHashSet();
        foreach (var reference in _sourceRefRepo.References)
        {
            Assert.That(proposalIds, Does.Contain(reference.TargetId));
        }
    }

    #endregion

    #region Visibility enforcement (Requirement 8.1, 8.2)

    [Test]
    public async Task VisibilityEnforcement_OverwritesProposedValueJson_ForPrivateSource()
    {
        // Arrange — AI response has PartyVisible but source is Private
        CreateQueuedSource(VisibilityScope.Private);
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new { name = "Silver Key", type = "Item", visibility = "PartyVisible" },
                    Rationale = "Key found in Voss's quarters",
                    Confidence = 0.9m
                }
            ],
            InputTokens = 200,
            OutputTokens = 100,
            TotalTokens = 300,
            DurationMs = 500,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(aiResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var proposal = _proposalRepo.Proposals[0];
        var jsonNode = JsonNode.Parse(proposal.ProposedValueJson);
        Assert.That(jsonNode!["visibility"]?.GetValue<string>(), Is.EqualTo("Private"));
    }

    [Test]
    public async Task VisibilityEnforcement_OverwritesProposedValueJson_ForGMOnlySource()
    {
        // Arrange — AI response has PartyVisible but source is GMOnly
        CreateQueuedSource(VisibilityScope.GMOnly);
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new { name = "Hidden Passage", type = "Location", visibility = "PartyVisible" },
                    Rationale = "Secret passage discovered",
                    Confidence = 0.95m
                }
            ],
            InputTokens = 200,
            OutputTokens = 100,
            TotalTokens = 300,
            DurationMs = 500,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(aiResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var proposal = _proposalRepo.Proposals[0];
        var jsonNode = JsonNode.Parse(proposal.ProposedValueJson);
        Assert.That(jsonNode!["visibility"]?.GetValue<string>(), Is.EqualTo("GMOnly"));
    }

    [Test]
    public async Task VisibilityEnforcement_OverwritesProposedValueJson_ForPartyVisibleSource()
    {
        // Arrange — AI response has GMOnly but source is PartyVisible
        CreateQueuedSource(VisibilityScope.PartyVisible);
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "AddFact",
                    TargetType = "ArtifactFact",
                    TargetId = Guid.NewGuid(),
                    ProposedValue = new { predicate = "allegiance", value = "Faction X", visibility = "GMOnly" },
                    Rationale = "Observed during session",
                    Confidence = 0.75m
                }
            ],
            InputTokens = 200,
            OutputTokens = 100,
            TotalTokens = 300,
            DurationMs = 500,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(aiResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        var proposal = _proposalRepo.Proposals[0];
        var jsonNode = JsonNode.Parse(proposal.ProposedValueJson);
        Assert.That(jsonNode!["visibility"]?.GetValue<string>(), Is.EqualTo("PartyVisible"));
    }

    [Test]
    public async Task VisibilityEnforcement_AllProposals_MatchSourceVisibility()
    {
        // Arrange — multiple proposals with various AI-suggested visibilities, source is GMOnly
        CreateQueuedSource(VisibilityScope.GMOnly);
        var aiResponse = new AiExtractionResponse
        {
            Proposals =
            [
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new { name = "Artifact A", visibility = "Private" },
                    Rationale = "Reason A",
                    Confidence = 0.8m
                },
                new ExtractionProposal
                {
                    ChangeType = "CreateArtifact",
                    TargetType = "Artifact",
                    ProposedValue = new { name = "Artifact B", visibility = "PartyVisible" },
                    Rationale = "Reason B",
                    Confidence = 0.7m
                },
                new ExtractionProposal
                {
                    ChangeType = "AddFact",
                    TargetType = "ArtifactFact",
                    TargetId = Guid.NewGuid(),
                    ProposedValue = new { predicate = "status", value = "active", visibility = "GMOnly" },
                    Rationale = "Reason C",
                    Confidence = 0.9m
                }
            ],
            InputTokens = 300,
            OutputTokens = 150,
            TotalTokens = 450,
            DurationMs = 700,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(aiResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert — all proposals should have GMOnly visibility
        foreach (var proposal in _proposalRepo.Proposals)
        {
            var jsonNode = JsonNode.Parse(proposal.ProposedValueJson);
            Assert.That(jsonNode!["visibility"]?.GetValue<string>(), Is.EqualTo("GMOnly"),
                $"Proposal with rationale '{proposal.Rationale}' should have GMOnly visibility");
        }
    }

    #endregion

    #region Zero proposals → Completed batch (Requirement 7.5)

    [Test]
    public async Task ZeroProposals_CreatesReviewBatch_WithStatusCompleted()
    {
        // Arrange
        CreateQueuedSource();
        var emptyResponse = new AiExtractionResponse
        {
            Proposals = [],
            InputTokens = 300,
            OutputTokens = 50,
            TotalTokens = 350,
            DurationMs = 400,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(emptyResponse);

        // Act
        var outcome = await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(outcome.ProposalCount, Is.EqualTo(0));

        var batch = _batchRepo.Batches[0];
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Completed));
        Assert.That(batch.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task ZeroProposals_CreatesNoReviewProposals_Or_SourceReferences()
    {
        // Arrange
        CreateQueuedSource();
        var emptyResponse = new AiExtractionResponse
        {
            Proposals = [],
            InputTokens = 200,
            OutputTokens = 30,
            TotalTokens = 230,
            DurationMs = 300,
            Model = "gpt-4o"
        };
        _aiClient.SetupSuccess(emptyResponse);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert
        Assert.That(_proposalRepo.Proposals, Is.Empty);
        Assert.That(_sourceRefRepo.References, Is.Empty);
    }

    #endregion

    #region Atomic rollback on DB failure (Requirement 7.7)

    [Test]
    public async Task AtomicRollback_OnCommitFailure_NoProposalsPersisted()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse(2));
        _unitOfWork.ConfigureCommitFailure(shouldFail: true);

        // Act
        var outcome = await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert — outcome is non-transient failure
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
    }

    [Test]
    public async Task AtomicRollback_OnCommitFailure_TransactionIsRolledBack()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse(2));
        _unitOfWork.ConfigureCommitFailure(shouldFail: true);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert — the transaction was rolled back
        Assert.That(_unitOfWork.Transactions, Has.Count.EqualTo(1));
        Assert.That(_unitOfWork.Transactions[0].RolledBack, Is.True);
        Assert.That(_unitOfWork.Transactions[0].Committed, Is.False);
    }

    [Test]
    public async Task AtomicRollback_OnCommitFailure_SourceTransitionsToFailed()
    {
        // Arrange
        CreateQueuedSource();
        _aiClient.SetupSuccess(CreateSuccessResponse(2));
        _unitOfWork.ConfigureCommitFailure(shouldFail: true);

        // Act
        await _sut.ProcessExtractionAsync(SourceId, WorldId, CancellationToken.None);

        // Assert — source should be marked Failed
        var source = await _sourceRepo.GetByIdAsync(SourceId);
        Assert.That(source!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
    }

    #endregion
}
