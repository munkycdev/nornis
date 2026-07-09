using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ExtractionServiceContextAssemblyTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _reviewBatchRepository = null!;
    private InMemoryReviewProposalRepository _reviewProposalRepository = null!;
    private InMemorySourceReferenceRepository _sourceReferenceRepository = null!;
    private InMemoryAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _artifactFactRepository = null!;
    private FakeAiExtractionClient _aiClient = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ExtractionOptions _options = null!;
    private ExtractionService _sut = null!;

    private static readonly Guid CampaignId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _reviewBatchRepository = new InMemoryReviewBatchRepository();
        _reviewProposalRepository = new InMemoryReviewProposalRepository();
        _sourceReferenceRepository = new InMemorySourceReferenceRepository();
        _aiUsageRecordRepository = new InMemoryAiUsageRecordRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _artifactFactRepository = new InMemoryArtifactFactRepository();
        _aiClient = new FakeAiExtractionClient();
        _unitOfWork = new FakeUnitOfWork();
        _options = new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2,
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new() { InputPerMillionTokensUsd = 2.50m, OutputPerMillionTokensUsd = 10.00m }
            }
        };

        _sut = CreateService();
    }

    private ExtractionService CreateService() => new(
        _sourceRepository,
        _reviewBatchRepository,
        _reviewProposalRepository,
        _sourceReferenceRepository,
        _aiUsageRecordRepository,
        _artifactRepository,
        _artifactFactRepository,
        _aiClient,
        _unitOfWork,
        Options.Create(_options),
        NullLogger<ExtractionService>.Instance);

    private Source CreateQueuedSource(
        string body = "Captain Voss met the party in Black Harbor.",
        VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        return new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            Type = SourceType.SessionNote,
            Title = "Session 5 Notes",
            Body = body,
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid(),
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Queued
        };
    }

    private Artifact CreateArtifact(
        string name,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        DateTimeOffset? updatedAt = null)
    {
        return new Artifact
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            Type = ArtifactType.Character,
            Name = name,
            Summary = $"{name} summary",
            Visibility = visibility,
            Confidence = 0.9m,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow.AddDays(-1)
        };
    }

    private void ConfigureSuccessfulAiResponse(int proposalCount = 1)
    {
        var proposals = Enumerable.Range(0, proposalCount).Select(i => new ExtractionProposal
        {
            ChangeType = "CreateArtifact",
            TargetType = "Artifact",
            TargetId = null,
            ProposedValue = new { name = $"Artifact {i}", type = "Character", visibility = "PartyVisible" },
            Rationale = $"Found in source text ({i})",
            Confidence = 0.85m
        }).ToList();

        _aiClient.SetupSuccess(new AiExtractionResponse
        {
            Proposals = proposals,
            InputTokens = 500,
            OutputTokens = 200,
            TotalTokens = 700,
            DurationMs = 1200,
            Model = "gpt-4o"
        });
    }

    [Test]
    public async Task RecentArtifacts_LimitedTo_MaxArtifactContextCount()
    {
        // Arrange: create more artifacts than MaxArtifactContextCount
        _options.MaxArtifactContextCount = 5;
        _sut = CreateService();

        var source = CreateQueuedSource("No artifact names match here for sure.");
        _sourceRepository.Seed(source);

        // Seed 10 artifacts — only 5 should appear in context
        for (var i = 0; i < 10; i++)
        {
            _artifactRepository.Seed(CreateArtifact(
                $"Artifact{i}UniqueNameXYZ",
                updatedAt: DateTimeOffset.UtcNow.AddHours(-i)));
        }

        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: The AI client should receive at most 5 artifacts in context
        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1));
        var request = _aiClient.Requests[0];
        Assert.That(request.ExistingArtifacts.Count, Is.LessThanOrEqualTo(5));
    }

    [Test]
    public async Task NameMatchedArtifacts_PrioritizedFirst_InMergedList()
    {
        // Arrange: create some name-matched and some recent artifacts
        _options.MaxArtifactContextCount = 5;
        _sut = CreateService();

        var source = CreateQueuedSource("Captain Voss and Silver Key are important.");
        _sourceRepository.Seed(source);

        // Name-matched artifacts (their names appear in the source body)
        var voss = CreateArtifact("Captain Voss", updatedAt: DateTimeOffset.UtcNow.AddDays(-5));
        var silverKey = CreateArtifact("Silver Key", updatedAt: DateTimeOffset.UtcNow.AddDays(-3));

        // Recent artifacts (more recently updated but names not in source body)
        var recent1 = CreateArtifact("Black Harbor Dock", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var recent2 = CreateArtifact("Tavrin Shield", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var recent3 = CreateArtifact("Missing Caravan", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        _artifactRepository.Seed(voss, silverKey, recent1, recent2, recent3);
        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: Name-matched (Captain Voss, Silver Key) should come first
        var request = _aiClient.Requests[0];
        var contextNames = request.ExistingArtifacts.Select(a => a.Name).ToList();

        Assert.That(contextNames, Has.Count.EqualTo(5));
        // Name-matched artifacts should be at the start
        var vossIndex = contextNames.IndexOf("Captain Voss");
        var silverKeyIndex = contextNames.IndexOf("Silver Key");
        var recentStartIndex = contextNames.IndexOf(contextNames.First(n =>
            n != "Captain Voss" && n != "Silver Key"));

        Assert.That(vossIndex, Is.LessThan(recentStartIndex),
            "Name-matched artifacts should appear before recent-only artifacts");
        Assert.That(silverKeyIndex, Is.LessThan(recentStartIndex),
            "Name-matched artifacts should appear before recent-only artifacts");
    }

    [Test]
    public async Task Deduplication_OverlappingArtifacts_NoRepeats()
    {
        // Arrange: create an artifact that is both name-matched AND recent
        var source = CreateQueuedSource("Captain Voss returned to Black Harbor.");
        _sourceRepository.Seed(source);

        // This artifact is name-matched (appears in body) and also the most recent
        var voss = CreateArtifact("Captain Voss", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var harbor = CreateArtifact("Black Harbor", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var other = CreateArtifact("Tavrin", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-3));

        _artifactRepository.Seed(voss, harbor, other);
        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: No duplicates — each artifact should appear exactly once
        var request = _aiClient.Requests[0];
        var contextIds = request.ExistingArtifacts.Select(a => a.Id).ToList();

        Assert.That(contextIds, Is.Unique, "Context should not contain duplicate artifacts");
        Assert.That(contextIds, Does.Contain(voss.Id));
        Assert.That(contextIds, Does.Contain(harbor.Id));
        Assert.That(contextIds, Does.Contain(other.Id));
    }

    [Test]
    public async Task VisibilityFiltering_PrivateSource_OnlyPrivateArtifacts()
    {
        // Arrange: Private source should only see Private artifacts
        var source = CreateQueuedSource("Captain Voss is suspicious.", VisibilityScope.Private);
        _sourceRepository.Seed(source);

        var privateArtifact = CreateArtifact("Captain Voss", VisibilityScope.Private,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var gmArtifact = CreateArtifact("GM Secret", VisibilityScope.GMOnly,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var partyArtifact = CreateArtifact("Party Knowledge", VisibilityScope.PartyVisible,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-3));

        _artifactRepository.Seed(privateArtifact, gmArtifact, partyArtifact);
        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: Only Private artifacts should appear in context
        var request = _aiClient.Requests[0];
        Assert.That(request.ExistingArtifacts.Count, Is.EqualTo(1));
        Assert.That(request.ExistingArtifacts[0].Id, Is.EqualTo(privateArtifact.Id));
    }

    [Test]
    public async Task VisibilityFiltering_GMOnlySource_IncludesGMOnlyAndPartyVisible()
    {
        // Arrange: GMOnly source should see GMOnly and PartyVisible artifacts
        var source = CreateQueuedSource("Captain Voss and Black Harbor are relevant.", VisibilityScope.GMOnly);
        _sourceRepository.Seed(source);

        var privateArtifact = CreateArtifact("Secret Notes", VisibilityScope.Private,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var gmArtifact = CreateArtifact("GM Secret", VisibilityScope.GMOnly,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var partyArtifact = CreateArtifact("Black Harbor", VisibilityScope.PartyVisible,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-3));

        _artifactRepository.Seed(privateArtifact, gmArtifact, partyArtifact);
        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: GMOnly and PartyVisible artifacts, but not Private
        var request = _aiClient.Requests[0];
        var contextIds = request.ExistingArtifacts.Select(a => a.Id).ToList();
        Assert.That(contextIds, Does.Contain(gmArtifact.Id));
        Assert.That(contextIds, Does.Contain(partyArtifact.Id));
        Assert.That(contextIds, Does.Not.Contain(privateArtifact.Id));
    }

    [Test]
    public async Task VisibilityFiltering_PartyVisibleSource_OnlyPartyVisibleArtifacts()
    {
        // Arrange: PartyVisible source should only see PartyVisible artifacts
        var source = CreateQueuedSource("Captain Voss is in town.", VisibilityScope.PartyVisible);
        _sourceRepository.Seed(source);

        var privateArtifact = CreateArtifact("Private Thought", VisibilityScope.Private,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var gmArtifact = CreateArtifact("GM Secret", VisibilityScope.GMOnly,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var partyArtifact = CreateArtifact("Captain Voss", VisibilityScope.PartyVisible,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-3));

        _artifactRepository.Seed(privateArtifact, gmArtifact, partyArtifact);
        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: Only PartyVisible artifacts
        var request = _aiClient.Requests[0];
        Assert.That(request.ExistingArtifacts.Count, Is.EqualTo(1));
        Assert.That(request.ExistingArtifacts[0].Id, Is.EqualTo(partyArtifact.Id));
    }

    [Test]
    public async Task FactsLimited_ToMaxFactsPerArtifact()
    {
        // Arrange: create an artifact with more facts than MaxFactsPerArtifact
        _options.MaxFactsPerArtifact = 3;
        _sut = CreateService();

        var source = CreateQueuedSource("Captain Voss did many things.");
        _sourceRepository.Seed(source);

        var artifact = CreateArtifact("Captain Voss", updatedAt: DateTimeOffset.UtcNow);
        _artifactRepository.Seed(artifact);

        // Seed 6 facts, ordered by UpdatedAt
        for (var i = 0; i < 6; i++)
        {
            _artifactFactRepository.Seed(new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifact.Id,
                Predicate = $"fact{i}",
                Value = $"value{i}",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-i) // most recent first
            });
        }

        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: Only 3 facts should appear in context
        var request = _aiClient.Requests[0];
        var artifactContext = request.ExistingArtifacts.Single(a => a.Id == artifact.Id);
        Assert.That(artifactContext.Facts.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task EmptyCampaign_NoArtifacts_ProceedsWithoutError()
    {
        // Arrange: campaign with no artifacts at all
        var source = CreateQueuedSource("Captain Voss sailed the stormy seas.");
        _sourceRepository.Seed(source);

        // No artifacts seeded
        ConfigureSuccessfulAiResponse();

        // Act
        var outcome = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: Service proceeds successfully, AI is called with empty context
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1));
        Assert.That(_aiClient.Requests[0].ExistingArtifacts, Is.Empty);
    }

    [Test]
    public async Task ArchivedArtifacts_ExcludedFromContext_ViaBothRetrievalPaths()
    {
        // Arrange: after a merge, the losing duplicate is Archived. It must not re-enter
        // extraction context via name-matching or recency, or the AI re-proposes the merge.
        var source = CreateQueuedSource("The Missing Caravan was spotted near Black Harbor.");
        _sourceRepository.Seed(source);

        // Name-matched path: archived artifact whose name appears in the source body
        var archivedNameMatched = CreateArtifact("Missing Caravan", updatedAt: DateTimeOffset.UtcNow.AddDays(-2));
        archivedNameMatched.Status = ArtifactStatus.Archived;

        // Recent path: archived artifact that is the most recently updated
        var archivedRecent = CreateArtifact("Tavrin Shield", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        archivedRecent.Status = ArtifactStatus.Archived;

        var active = CreateArtifact("Black Harbor", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        _artifactRepository.Seed(archivedNameMatched, archivedRecent, active);
        ConfigureSuccessfulAiResponse();

        // Act
        await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: only the active artifact appears in context
        var request = _aiClient.Requests[0];
        var contextIds = request.ExistingArtifacts.Select(a => a.Id).ToList();
        Assert.That(contextIds, Does.Not.Contain(archivedNameMatched.Id),
            "Archived artifacts must not appear in extraction context via name-matching");
        Assert.That(contextIds, Does.Not.Contain(archivedRecent.Id),
            "Archived artifacts must not appear in extraction context via recency");
        Assert.That(contextIds, Is.EquivalentTo(new[] { active.Id }));
    }

    [Test]
    public async Task NullSourceBody_SkipsNameMatching_ShortCircuitsToCompletedBatch()
    {
        // Arrange: source with null body triggers the empty body short-circuit
        // which means no AI call and no name-matching (Requirement 4.7)
        var source = CreateQueuedSource(body: null!, visibility: VisibilityScope.PartyVisible);
        _sourceRepository.Seed(source);

        // Seed artifacts that would match if name-matching occurred
        _artifactRepository.Seed(CreateArtifact("Captain Voss"));
        ConfigureSuccessfulAiResponse();

        // Act
        var outcome = await _sut.ProcessExtractionAsync(source.Id, CampaignId, CancellationToken.None);

        // Assert: AI was never called (body short-circuit), so no name-matching happened
        Assert.That(_aiClient.CallCount, Is.EqualTo(0),
            "AI client should not be called when source body is null");
        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        // A completed batch with zero proposals should exist
        Assert.That(_reviewBatchRepository.Batches, Has.Count.EqualTo(1));
        Assert.That(_reviewBatchRepository.Batches[0].Status, Is.EqualTo(ReviewBatchStatus.Completed));
    }
}
