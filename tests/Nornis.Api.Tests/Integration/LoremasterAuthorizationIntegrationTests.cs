using System.Net;
using System.Net.Http.Json;
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
/// Integration tests for LoremasterController authorization and role-based visibility filtering.
/// These tests exercise the full HTTP pipeline: JWT validation, WorldMemberActionFilter,
/// knowledge retrieval with visibility filtering, and response assembly.
///
/// Validates: Requirements 1.1, 1.2, 1.3, 3.1, 3.2, 3.3, 3.4
/// </summary>
[TestFixture]
public class LoremasterAuthorizationIntegrationTests
{
    private LoremasterAuthorizationTestFactory _factory = null!;
    private LoremasterTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new LoremasterAuthorizationTestFactory();
        _scenario = await SetupLoremasterScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _scenario.ObserverClient.Dispose();
        _scenario.NonMemberClient.Dispose();
        _factory.Dispose();
    }

    private string AskUrl => $"/api/worlds/{_scenario.World.Id}/ask";

    #region Authorization Tests

    [Test]
    public async Task Ask_WithoutJwt_Returns401()
    {
        // Arrange — unauthenticated client (no bearer token)
        var client = _factory.CreateClient();

        var request = new AskLoremasterRequest("Who is Captain Voss?");

        // Act
        var response = await client.PostAsJsonAsync(AskUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Ask_NonMember_Returns403WithoutRevealingWorldExistence()
    {
        // Arrange — user who is not a member of the world
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        // Act
        var response = await _scenario.NonMemberClient.PostAsJsonAsync(AskUrl, request);

        // Assert — 403, not 404 (but importantly, same response for both existing and non-existing worlds)
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // Also verify that requesting a non-existent world returns the same status
        var nonExistentWorldUrl = $"/api/worlds/{Guid.NewGuid()}/ask";
        var response2 = await _scenario.NonMemberClient.PostAsJsonAsync(nonExistentWorldUrl, request);
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Ask_WorldMemberActionFilter_AppliedToEndpoint()
    {
        // Arrange — GM user who IS a member
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        // Act — GM should be able to ask successfully
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);

        // Assert — should get 200 (not 403)
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    #endregion

    #region GM Visibility Tests

    [Test]
    public async Task Ask_GmRole_ReceivesGmOnlyContentInAnswer()
    {
        // Arrange — GM asks about the GMOnly artifact
        var request = new AskLoremasterRequest("Tell me about SecretGmPlot");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(AskUrl, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);

        // Assert — The answer should contain the GMOnly artifact name because
        // the fake AI client echoes back the prompt content (which includes artifact names)
        Assert.That(answer!.Answer, Does.Contain("SecretGmPlot"));
    }

    #endregion

    #region Player Visibility Tests

    [Test]
    public async Task Ask_PlayerRole_DoesNotReceiveGmOnlyContent()
    {
        // Arrange — Player asks about the GMOnly artifact by name
        var request = new AskLoremasterRequest("Tell me about SecretGmPlot");

        // Act
        var response = await _scenario.PlayerClient.PostAsJsonAsync(AskUrl, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);

        // Assert — The answer should NOT contain the GMOnly artifact name
        // because the Player's visibility filter excludes GMOnly content
        Assert.That(answer!.Answer, Does.Not.Contain("SecretGmPlot"));
    }

    [Test]
    public async Task Ask_PlayerRole_ReceivesPartyVisibleContent()
    {
        // Arrange — Player asks about a PartyVisible artifact
        var request = new AskLoremasterRequest("Tell me about CaptainVoss");

        // Act
        var response = await _scenario.PlayerClient.PostAsJsonAsync(AskUrl, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);

        // Assert — Player should see PartyVisible content
        Assert.That(answer!.Answer, Does.Contain("CaptainVoss"));
    }

    #endregion

    #region Observer Visibility Tests

    [Test]
    public async Task Ask_ObserverRole_ReceivesOnlyPartyVisibleContent()
    {
        // Arrange — Observer asks about various artifacts
        var request = new AskLoremasterRequest("Tell me about CaptainVoss and SecretGmPlot");

        // Act
        var response = await _scenario.ObserverClient.PostAsJsonAsync(AskUrl, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);

        // Assert — Observer sees PartyVisible but not GMOnly
        Assert.That(answer!.Answer, Does.Contain("CaptainVoss"));
        Assert.That(answer.Answer, Does.Not.Contain("SecretGmPlot"));
    }

    #endregion

    #region Private Content Tests

    [Test]
    public async Task Ask_PrivateFactsOfOtherUsers_NeverAppearInAnswers()
    {
        // Arrange — Player asks about the PartyVisible artifact that has a GMOnly fact attached.
        // The GMOnly fact (secret_motive: "Plans to betray the party") should not appear for Player.
        var request = new AskLoremasterRequest("Tell me about CaptainVoss");

        // Act
        var response = await _scenario.PlayerClient.PostAsJsonAsync(AskUrl, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);

        // Assert — The GMOnly fact content should not appear for Player
        Assert.That(answer!.Answer, Does.Not.Contain("Plans to betray the party"));
        // The PartyVisible fact should still be present
        Assert.That(answer.Answer, Does.Contain("Black Harbor"));
    }

    [Test]
    public async Task Ask_GmOnlyFactsOfArtifacts_NeverAppearForObserver()
    {
        // Arrange — Observer asks about the PartyVisible artifact
        var request = new AskLoremasterRequest("Tell me about CaptainVoss");

        // Act
        var response = await _scenario.ObserverClient.PostAsJsonAsync(AskUrl, request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer, Is.Not.Null);

        // Assert — The GMOnly fact content should not appear for Observer
        Assert.That(answer!.Answer, Does.Not.Contain("Plans to betray the party"));
        // The PartyVisible fact should still be present
        Assert.That(answer.Answer, Does.Contain("Black Harbor"));
    }

    #endregion

    #region Test Setup

    /// <summary>
    /// Sets up a complete Loremaster test scenario with:
    /// - A world
    /// - A GM user (Kelda) with GM WorldMember
    /// - A Player user (Tavrin) with Player WorldMember
    /// - An Observer user (Jorin) with Observer WorldMember
    /// - A non-member user
    /// - Artifacts with different visibilities: PartyVisible, GMOnly, Private (owned by GM)
    /// </summary>
    private static async Task<LoremasterTestScenario> SetupLoremasterScenarioAsync(
        LoremasterAuthorizationTestFactory factory)
    {
        // Provision users
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-kelda-lore", "kelda@blackharbor.com", "Kelda");

        var playerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|player-tavrin-lore", "tavrin@blackharbor.com", "Tavrin");

        var observerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|observer-jorin-lore", "jorin@blackharbor.com", "Jorin");

        var nonMemberUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|nonmember-stranger", "stranger@elsewhere.com", "Stranger");

        // Create world with GM
        var world = await SourceTestHelpers.CreateTestWorldAsync(factory, gmUserId);

        // Add player and observer members
        await SourceTestHelpers.AddWorldMemberAsync(
            factory, world.Id, playerUserId, WorldRole.Player,
            displayName: "Tavrin", characterName: "Tavrin the Bold");

        await SourceTestHelpers.AddWorldMemberAsync(
            factory, world.Id, observerUserId, WorldRole.Observer,
            displayName: "Jorin");

        // Seed artifacts with different visibilities
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

            // PartyVisible artifact — visible to all members
            var partyVisibleArtifact = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                Name = "CaptainVoss",
                Type = ArtifactType.Character,
                Summary = "A harbor captain suspected of smuggling",
                Visibility = VisibilityScope.PartyVisible,
                Status = ArtifactStatus.Active,
                Confidence = 0.85m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(partyVisibleArtifact);

            // GMOnly artifact — visible only to GM
            var gmOnlyArtifact = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                Name = "SecretGmPlot",
                Type = ArtifactType.Event,
                Summary = "A secret plot only the GM knows about",
                Visibility = VisibilityScope.GMOnly,
                Status = ArtifactStatus.Active,
                Confidence = 0.9m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(gmOnlyArtifact);

            // Private artifact owned by GM — visible only to the GM
            var gmPrivateArtifact = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = world.Id,
                Name = "GmPrivateNote",
                Type = ArtifactType.Concept,
                Summary = "Private notes only the GM can see",
                Visibility = VisibilityScope.Private,
                Status = ArtifactStatus.Active,
                Confidence = 0.7m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(gmPrivateArtifact);

            // Add a PartyVisible fact to the party-visible artifact
            db.ArtifactFacts.Add(new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = partyVisibleArtifact.Id,
                Predicate = "location",
                Value = "Black Harbor",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.PartyVisible,
                Confidence = 0.9m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            // Add a GMOnly fact to the party-visible artifact (CaptainVoss)
            // This tests fact-level visibility filtering: Player/Observer shouldn't see this
            db.ArtifactFacts.Add(new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = partyVisibleArtifact.Id,
                Predicate = "secret_motive",
                Value = "Plans to betray the party",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.GMOnly,
                Confidence = 0.95m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            // Add a GMOnly fact to the GMOnly artifact
            db.ArtifactFacts.Add(new ArtifactFact
            {
                Id = Guid.NewGuid(),
                ArtifactId = gmOnlyArtifact.Id,
                Predicate = "secret_plot_detail",
                Value = "The betrayal happens at midnight",
                TruthState = TruthState.Confirmed,
                Visibility = VisibilityScope.GMOnly,
                Confidence = 0.95m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        return new LoremasterTestScenario
        {
            World = world,
            GmUserId = gmUserId,
            PlayerUserId = playerUserId,
            ObserverUserId = observerUserId,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-kelda-lore", email: "kelda@blackharbor.com", nickname: "Kelda"),
            PlayerClient = factory.CreateAuthenticatedClient(
                sub: "auth0|player-tavrin-lore", email: "tavrin@blackharbor.com", nickname: "Tavrin"),
            ObserverClient = factory.CreateAuthenticatedClient(
                sub: "auth0|observer-jorin-lore", email: "jorin@blackharbor.com", nickname: "Jorin"),
            NonMemberClient = factory.CreateAuthenticatedClient(
                sub: "auth0|nonmember-stranger", email: "stranger@elsewhere.com", nickname: "Stranger")
        };
    }

    #endregion
}

/// <summary>
/// Contains all entities and pre-configured HTTP clients for Loremaster integration tests.
/// </summary>
public class LoremasterTestScenario
{
    public required World World { get; init; }
    public required Guid GmUserId { get; init; }
    public required Guid PlayerUserId { get; init; }
    public required Guid ObserverUserId { get; init; }
    public required HttpClient GmClient { get; init; }
    public required HttpClient PlayerClient { get; init; }
    public required HttpClient ObserverClient { get; init; }
    public required HttpClient NonMemberClient { get; init; }
}

/// <summary>
/// A WebApplicationFactory that replaces ILoremasterAiClient with an echo-back fake
/// that returns the user message content from the prompt as the answer text.
/// This enables visibility tests — if an artifact name appears in the AI answer,
/// it means the knowledge retriever included it in the context for that user's role.
/// </summary>
public class LoremasterAuthorizationTestFactory : NornisWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Replace ILoremasterAiClient with a fake that echoes the prompt content
            var aiClientDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ILoremasterAiClient));
            if (aiClientDescriptor is not null)
            {
                services.Remove(aiClientDescriptor);
            }

            services.AddScoped<ILoremasterAiClient, EchoLoremasterAiClient>();
        });
    }
}

/// <summary>
/// A fake AI client that echoes back ONLY the knowledge context section from the prompt.
/// The user message has the format:
/// "## World Knowledge Context\n...\n## Question\n..."
/// We extract just the context part (before "## Question") so that assertions on the answer
/// only reflect what the knowledge retriever included, not the question text itself.
/// </summary>
public class EchoLoremasterAiClient : ILoremasterAiClient
{
    public Task<LoremasterAiResponse> AskAsync(LoremasterAiRequest request, CancellationToken ct)
    {
        // Extract only the knowledge context section (before "## Question")
        var userMessage = request.UserMessage;
        var questionMarker = "## Question";
        var questionIndex = userMessage.IndexOf(questionMarker, StringComparison.Ordinal);

        var knowledgeContext = questionIndex > 0
            ? userMessage[..questionIndex]
            : userMessage;

        return Task.FromResult(new LoremasterAiResponse
        {
            AnswerText = knowledgeContext,
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            DurationMs = 200,
            Model = "gpt-4o"
        });
    }
}
