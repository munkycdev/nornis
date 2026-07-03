using Microsoft.Extensions.DependencyInjection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Helper methods for setting up test data for source integration tests.
/// Operates directly on the in-memory database, bypassing the API layer
/// to create preconditions without coupling test setup to endpoint behavior.
/// </summary>
public static class SourceTestHelpers
{
    /// <summary>
    /// Creates a user directly in the database and returns the generated User entity.
    /// Use this when you need a user that already exists without making an HTTP request.
    /// </summary>
    public static async Task<User> CreateTestUserAsync(
        NornisWebApplicationFactory factory,
        string auth0Sub,
        string email,
        string? username = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = auth0Sub,
            Email = email,
            Username = username ?? email.Split('@')[0],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Provisions a user by making an authenticated request (triggering the UserProvisioningMiddleware),
    /// then returns their Nornis User ID from the database.
    /// </summary>
    public static async Task<Guid> ProvisionUserAndGetIdAsync(
        NornisWebApplicationFactory factory,
        string auth0Sub,
        string email,
        string? nickname = null)
    {
        var client = factory.CreateAuthenticatedClient(sub: auth0Sub, email: email, nickname: nickname);
        await client.GetAsync("/api/campaigns");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = db.Users.First(u => u.Auth0SubjectId == auth0Sub);
        return user.Id;
    }

    /// <summary>
    /// Creates a campaign directly in the database and returns the Campaign entity.
    /// Also creates the specified user as a GM member of the campaign.
    /// </summary>
    public static async Task<Campaign> CreateTestCampaignAsync(
        NornisWebApplicationFactory factory,
        Guid createdByUserId,
        string name = "Black Harbor Investigation",
        string? description = "A dark mystery in the harbor district",
        string? gameSystem = "D&D 5e")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            GameSystem = gameSystem,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Campaigns.Add(campaign);

        // Add the creator as a GM member
        db.CampaignMembers.Add(new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            UserId = createdByUserId,
            Role = CampaignRole.GM,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return campaign;
    }

    /// <summary>
    /// Adds a campaign member with the specified role directly in the database.
    /// The user must already exist in the database.
    /// </summary>
    public static async Task<CampaignMember> AddCampaignMemberAsync(
        NornisWebApplicationFactory factory,
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        string? displayName = null,
        string? characterName = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var member = new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            UserId = userId,
            Role = role,
            DisplayName = displayName,
            CharacterName = characterName,
            JoinedAt = DateTimeOffset.UtcNow
        };

        db.CampaignMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    /// <summary>
    /// Creates a source directly in the database, bypassing the API.
    /// Useful for setting up preconditions (e.g., a source already in a specific processing state).
    /// </summary>
    public static async Task<Source> CreateTestSourceAsync(
        NornisWebApplicationFactory factory,
        Guid campaignId,
        Guid createdByUserId,
        string title = "Session 4 — Questioning Captain Voss",
        SourceType type = SourceType.SessionNote,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        SourceProcessingStatus processingStatus = SourceProcessingStatus.Draft,
        string? body = null,
        string? uri = null,
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? createdAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var source = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            CreatedByUserId = createdByUserId,
            Title = title,
            Type = type,
            Visibility = visibility,
            ProcessingStatus = processingStatus,
            Body = body,
            Uri = uri,
            OccurredAt = occurredAt,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };

        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    /// <summary>
    /// Sets up a complete test scenario with a campaign, GM user, and optionally a player and observer.
    /// Returns all created entities for use in assertions.
    /// </summary>
    public static async Task<SourceTestScenario> SetupFullScenarioAsync(
        NornisWebApplicationFactory factory)
    {
        // Create users via provisioning (so they work with authenticated HTTP clients)
        var gmUserId = await ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-kelda-source", "kelda@blackharbor.com", "Kelda");

        var playerUserId = await ProvisionUserAndGetIdAsync(
            factory, "auth0|player-tavrin-source", "tavrin@blackharbor.com", "Tavrin");

        var observerUserId = await ProvisionUserAndGetIdAsync(
            factory, "auth0|observer-jorin-source", "jorin@blackharbor.com", "Jorin");

        // Create campaign with GM
        var campaign = await CreateTestCampaignAsync(factory, gmUserId);

        // Add player and observer
        await AddCampaignMemberAsync(factory, campaign.Id, playerUserId, CampaignRole.Player,
            displayName: "Tavrin", characterName: "Tavrin the Bold");

        await AddCampaignMemberAsync(factory, campaign.Id, observerUserId, CampaignRole.Observer,
            displayName: "Jorin");

        return new SourceTestScenario
        {
            Campaign = campaign,
            GmUserId = gmUserId,
            PlayerUserId = playerUserId,
            ObserverUserId = observerUserId,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-kelda-source", email: "kelda@blackharbor.com", nickname: "Kelda"),
            PlayerClient = factory.CreateAuthenticatedClient(
                sub: "auth0|player-tavrin-source", email: "tavrin@blackharbor.com", nickname: "Tavrin"),
            ObserverClient = factory.CreateAuthenticatedClient(
                sub: "auth0|observer-jorin-source", email: "jorin@blackharbor.com", nickname: "Jorin")
        };
    }
}

/// <summary>
/// Contains all entities and pre-configured HTTP clients for a complete source test scenario.
/// </summary>
public class SourceTestScenario
{
    public required Campaign Campaign { get; init; }
    public required Guid GmUserId { get; init; }
    public required Guid PlayerUserId { get; init; }
    public required Guid ObserverUserId { get; init; }
    public required HttpClient GmClient { get; init; }
    public required HttpClient PlayerClient { get; init; }
    public required HttpClient ObserverClient { get; init; }
}
