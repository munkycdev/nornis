using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// Full-stack wiring for "remove an artifact from canon": the world-membership filter, the
/// GM check, the cascade service, and the DB delete, over real HTTP.
/// </summary>
[TestFixture]
public class ArtifactRemovalFlowTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Guid> CreateWorldAsGm(HttpClient gmClient)
    {
        var response = await gmClient.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Black Harbor Investigation", "A mystery", "D&D 5e"));
        response.EnsureSuccessStatusCode();
        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        return world!.Id;
    }

    private async Task<Guid> SeedArtifactWithFact(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var artifactId = Guid.NewGuid();
        db.Artifacts.Add(new Artifact
        {
            Id = artifactId,
            WorldId = worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        });
        db.ArtifactFacts.Add(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = "location",
            Value = "at sea",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        });
        await db.SaveChangesAsync();
        return artifactId;
    }

    [Test]
    public async Task RemovalPreview_ThenDelete_RemovesArtifactFromCanon()
    {
        var gm = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-remove-1", email: "gm@example.com", nickname: "GM");
        var worldId = await CreateWorldAsGm(gm);
        var artifactId = await SeedArtifactWithFact(worldId);

        var preview = await gm.GetAsync($"/api/worlds/{worldId}/artifacts/{artifactId}/removal-preview");
        Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var previewBody = await preview.Content.ReadFromJsonAsync<ArtifactRemovalPreviewResponse>();
        Assert.That(previewBody!.ArtifactName, Is.EqualTo("Captain Voss"));
        Assert.That(previewBody.FactCount, Is.EqualTo(1));

        var delete = await gm.DeleteAsync($"/api/worlds/{worldId}/artifacts/{artifactId}");
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Artifact and its fact are gone.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(await db.Artifacts.AnyAsync(a => a.Id == artifactId), Is.False);
        Assert.That(await db.ArtifactFacts.AnyAsync(f => f.ArtifactId == artifactId), Is.False);

        // And GET now 404s.
        var get = await gm.GetAsync($"/api/worlds/{worldId}/artifacts/{artifactId}");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_ByNonMember_Returns403()
    {
        var gm = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-remove-2", email: "gm@example.com", nickname: "GM");
        var worldId = await CreateWorldAsGm(gm);
        var artifactId = await SeedArtifactWithFact(worldId);

        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-2", email: "out@example.com", nickname: "Outsider");
        await outsider.GetAsync("/api/worlds"); // provision

        var response = await outsider.DeleteAsync($"/api/worlds/{worldId}/artifacts/{artifactId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // Still present.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(await db.Artifacts.AnyAsync(a => a.Id == artifactId), Is.True);
    }
}
