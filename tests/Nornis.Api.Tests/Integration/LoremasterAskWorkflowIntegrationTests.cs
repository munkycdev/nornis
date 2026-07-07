using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Integration;

/// <summary>
/// Integration tests for the full Ask Loremaster workflow through HTTP.
/// These tests exercise the complete pipeline: authentication, campaign membership,
/// knowledge retrieval, AI invocation (via fake), citation parsing, confidence
/// calculation, usage tracking, and response serialization.
///
/// Validates: Requirements 2.1, 2.2, 5.1, 8.1, 8.2, 9.1, 10.1
/// </summary>
[TestFixture]
public class LoremasterAskWorkflowIntegrationTests
{
    private LoremasterAskTestFactory _factory = null!;
    private AskWorkflowScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new LoremasterAskTestFactory();

        // Force the factory to build the host so services are available
        _ = _factory.CreateClient();

        _scenario = await SetupAskScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _factory.Dispose();
    }

    private string AskUrl => $"/api/campaigns/{_scenario.Campaign.Id}/ask";

    #region Valid Question → 200 with answer, citations, confidence, caveats

    [Test]
    public async Task Post_ValidQuestion_Returns200WithStructuredAnswer()
    {
        // Arrange — configure the fake AI to return a response with citation markers
        var artifactRefId = $"art-{_scenario.CaptainVoss.Id.ToString()[..8]}";
        _factory.FakeAiClient.SetupSuccess(
            $"Captain Voss is a sea captain based in Black Harbor [ref:{artifactRefId}]. " +
            "He is suspected of involvement in the missing caravan.");

        var request = new AskLoremasterRequest("Who is Captain Voss?");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Is.Not.Null.And.Not.Empty);
        Assert.That(answer.Confidence, Is.Not.Null.And.Not.Empty);
        Assert.That(answer.Confidence, Is.AnyOf("High", "Medium", "Low"));
        Assert.That(answer.Caveats, Is.Not.Null);
        Assert.That(answer.Citations, Is.Not.Null);
    }

    #endregion

    #region Question with matching artifact names → relevant knowledge in answer

    [Test]
    public async Task Post_QuestionWithMatchingArtifactNames_RetrievesRelevantKnowledge()
    {
        // Arrange — configure AI to echo back artifact names it receives in prompt
        _factory.FakeAiClient.SetupSuccess(
            "Captain Voss is known to operate out of Black Harbor. " +
            "The Silver Key was found in his quarters.");

        var request = new AskLoremasterRequest(
            "Tell me about Captain Voss and the Silver Key");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert — successful response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.Answer, Does.Contain("Captain Voss"));

        // Verify the AI client received a prompt containing the artifact names
        Assert.That(_factory.FakeAiClient.CallCount, Is.EqualTo(1));
        var aiRequest = _factory.FakeAiClient.LastRequest!;
        Assert.That(aiRequest.UserMessage, Does.Contain("Captain Voss").Or.Contain("Silver Key"));
    }

    #endregion

    #region Empty question → 400

    [Test]
    public async Task Post_EmptyQuestion_Returns400()
    {
        // Arrange
        var request = new AskLoremasterRequest("");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.Not.Null.And.Not.Empty);
        Assert.That(error.Message, Is.Not.Null.And.Not.Empty);

        // Verify AI was NOT called
        Assert.That(_factory.FakeAiClient.CallCount, Is.EqualTo(0));
    }

    #endregion

    #region Oversized question → 400

    [Test]
    public async Task Post_OversizedQuestion_Returns400()
    {
        // Arrange — question exceeds 2000 characters
        var oversizedQuestion = new string('x', 2001);
        var request = new AskLoremasterRequest(oversizedQuestion);

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.Not.Null.And.Not.Empty);
        Assert.That(error.Message, Is.Not.Null.And.Not.Empty);

        // Verify AI was NOT called
        Assert.That(_factory.FakeAiClient.CallCount, Is.EqualTo(0));
    }

    #endregion

    #region AI timeout → 503

    [Test]
    public async Task Post_AiTimeout_Returns503()
    {
        // Arrange — configure AI to simulate a timeout
        _factory.FakeAiClient.SetupTimeout();
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.Not.Null.And.Not.Empty);
        Assert.That(error.Message, Is.Not.Null.And.Not.Empty);
        // Error message should not expose internals
        Assert.That(error.Message, Does.Not.Contain("OperationCanceledException"));
        Assert.That(error.Message, Does.Not.Contain("stack trace"));
    }

    #endregion

    #region AiUsageRecord created in database after successful call

    [Test]
    public async Task Post_SuccessfulAsk_CreatesAiUsageRecordInDatabase()
    {
        // Arrange
        _factory.FakeAiClient.SetupSuccess("Captain Voss operates in Black Harbor.");
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert — request succeeded
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify AiUsageRecord was created in the database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var usageRecords = db.AiUsageRecords
            .Where(r => r.CampaignId == _scenario.Campaign.Id
                        && r.OperationType == AiOperationType.AskLoremaster)
            .ToList();

        Assert.That(usageRecords, Has.Count.EqualTo(1));

        var record = usageRecords[0];
        Assert.That(record.CampaignId, Is.EqualTo(_scenario.Campaign.Id));
        Assert.That(record.UserId, Is.EqualTo(_scenario.GmUserId));
        Assert.That(record.OperationType, Is.EqualTo(AiOperationType.AskLoremaster));
        Assert.That(record.Model, Is.Not.Null.And.Not.Empty);
        Assert.That(record.Succeeded, Is.True);
        Assert.That(record.InputTokens, Is.GreaterThan(0));
        Assert.That(record.OutputTokens, Is.GreaterThan(0));
        Assert.That(record.TotalTokens, Is.GreaterThan(0));
        Assert.That(record.DurationMs, Is.GreaterThanOrEqualTo(0));
        Assert.That(record.EstimatedCostUsd, Is.GreaterThan(0));
    }

    #endregion

    #region Scenario Setup

    /// <summary>
    /// Sets up the full Ask workflow test scenario:
    /// - Campaign "Black Harbor Investigation"
    /// - GM user (Kelda) with CampaignMember
    /// - Artifacts: Captain Voss, Black Harbor, Silver Key (all PartyVisible)
    /// - Facts: location, occupation (Confirmed/Likely truth state)
    /// - Source references for those facts
    /// </summary>
    private static async Task<AskWorkflowScenario> SetupAskScenarioAsync(
        LoremasterAskTestFactory factory)
    {
        // Provision the GM user
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-kelda-ask", "kelda@blackharbor.com", "Kelda");

        // Create the campaign
        var campaign = await SourceTestHelpers.CreateTestCampaignAsync(factory, gmUserId);

        // Create a source for citations
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            factory, campaign.Id, gmUserId,
            title: "Session 4 — Questioning Captain Voss",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Processed,
            body: "We questioned Captain Voss in Black Harbor. He denied knowing about the missing caravan, but Tavrin found the Silver Key in his quarters.");

        // Seed artifacts, facts, and source references directly in the database
        Artifact captainVoss, blackHarbor, silverKey;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

            captainVoss = new Artifact
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                Type = ArtifactType.Character,
                Name = "Captain Voss",
                Summary = "A harbor captain suspected of smuggling",
                Visibility = VisibilityScope.PartyVisible,
                Confidence = 0.85m,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(captainVoss);

            blackHarbor = new Artifact
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                Type = ArtifactType.Location,
                Name = "Black Harbor",
                Summary = "A dark port city on the coast",
                Visibility = VisibilityScope.PartyVisible,
                Confidence = 0.9m,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(blackHarbor);

            silverKey = new Artifact
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                Type = ArtifactType.Item,
                Name = "Silver Key",
                Summary = "An ornate silver key found in Voss's quarters",
                Visibility = VisibilityScope.PartyVisible,
                Confidence = 0.8m,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(silverKey);

            // Facts for Captain Voss
            var vossLocationFact = new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = captainVoss.Id,
                Predicate = "location",
                Value = "Black Harbor",
                Confidence = 0.9m,
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ArtifactFacts.Add(vossLocationFact);

            var vossOccupationFact = new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = captainVoss.Id,
                Predicate = "occupation",
                Value = "Sea captain",
                Confidence = 0.85m,
                TruthState = TruthState.Likely,
                Visibility = VisibilityScope.PartyVisible,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ArtifactFacts.Add(vossOccupationFact);

            // Source references for facts
            db.SourceReferences.Add(new SourceReference
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                TargetType = SourceReferenceTargetType.ArtifactFact,
                TargetId = vossLocationFact.Id,
                Quote = "We questioned Captain Voss in Black Harbor",
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.SourceReferences.Add(new SourceReference
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                TargetType = SourceReferenceTargetType.ArtifactFact,
                TargetId = vossOccupationFact.Id,
                Quote = "Captain Voss",
                CreatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        return new AskWorkflowScenario
        {
            Campaign = campaign,
            GmUserId = gmUserId,
            Source = source,
            CaptainVoss = captainVoss,
            BlackHarbor = blackHarbor,
            SilverKey = silverKey,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-kelda-ask", email: "kelda@blackharbor.com", nickname: "Kelda")
        };
    }

    #endregion
}

/// <summary>
/// A derived factory that replaces ILoremasterAiClient with a configurable FakeLoremasterAiClient
/// for integration testing of the full Ask workflow without live Azure OpenAI calls.
/// </summary>
public class LoremasterAskTestFactory : NornisWebApplicationFactory
{
    public FakeLoremasterAiClient FakeAiClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Keep this test independent of the machine-local appsettings.json: price the model
        // the fake AI client reports ("gpt-4o") so cost tracking has a matching entry.
        // (ModelPricing is keyed by the response's model; without a match, cost is $0.)
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Loremaster:ModelPricing:gpt-4o:InputPerMillionTokensUsd"] = "2.50",
                ["Loremaster:ModelPricing:gpt-4o:OutputPerMillionTokensUsd"] = "10.00",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real ILoremasterAiClient registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ILoremasterAiClient));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<ILoremasterAiClient>(FakeAiClient);
        });
    }
}

/// <summary>
/// Contains all entities and pre-configured HTTP clients for the Ask workflow integration tests.
/// </summary>
public class AskWorkflowScenario
{
    public required Campaign Campaign { get; init; }
    public required Guid GmUserId { get; init; }
    public required Source Source { get; init; }
    public required Artifact CaptainVoss { get; init; }
    public required Artifact BlackHarbor { get; init; }
    public required Artifact SilverKey { get; init; }
    public required HttpClient GmClient { get; init; }
}
