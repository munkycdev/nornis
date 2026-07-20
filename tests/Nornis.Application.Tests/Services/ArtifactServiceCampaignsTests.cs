using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactServiceCampaignsTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryCampaignRepository _campaignRepo = null!;
    private InMemoryStorylineCampaignRepository _storylineCampaignRepo = null!;
    private ArtifactService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _campaignRepo = new InMemoryCampaignRepository();
        _storylineCampaignRepo = new InMemoryStorylineCampaignRepository();
        _service = new ArtifactService(_artifactRepo, new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(), new InMemorySourceReferenceRepository(),
            new InMemorySourceRepository(), new InMemoryCharacterRepository(),
            new InMemoryWorldMemberRepository(), _storylineCampaignRepo, _campaignRepo);

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
    }

    private Artifact SeedArtifact(string name, ArtifactType type = ArtifactType.Storyline)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = type,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private Campaign SeedCampaign(string name, Guid? worldId = null)
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _worldId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _campaignRepo.Seed(campaign);
        return campaign;
    }

    private SetStorylineCampaignsCommand Command(Guid artifactId, IReadOnlyList<Guid> campaignIds, WorldRole role = WorldRole.GM) =>
        new(artifactId, _worldId, _gmUserId, role, campaignIds);

    [Test]
    public async Task SetCampaigns_DeclaresTheGivenCampaigns()
    {
        var storyline = SeedArtifact("Arc");
        var c1 = SeedCampaign("One");
        var c2 = SeedCampaign("Two");

        var result = await _service.SetStorylineCampaignsAsync(Command(storyline.Id, new[] { c1.Id, c2.Id }), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var declared = (await _storylineCampaignRepo.ListByArtifactIdAsync(storyline.Id, CancellationToken.None))
            .Select(l => l.CampaignId);
        Assert.That(declared, Is.EquivalentTo(new[] { c1.Id, c2.Id }));
    }

    [Test]
    public async Task SetCampaigns_ReplacesTheExistingSet()
    {
        var storyline = SeedArtifact("Arc");
        var c1 = SeedCampaign("One");
        var c2 = SeedCampaign("Two");
        _storylineCampaignRepo.Seed(storyline.Id, c1.Id);

        var result = await _service.SetStorylineCampaignsAsync(Command(storyline.Id, new[] { c2.Id }), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var declared = (await _storylineCampaignRepo.ListByArtifactIdAsync(storyline.Id, CancellationToken.None))
            .Select(l => l.CampaignId);
        Assert.That(declared, Is.EqualTo(new[] { c2.Id }));
    }

    [Test]
    public async Task SetCampaigns_EmptyListClearsEveryDeclaration()
    {
        var storyline = SeedArtifact("Arc");
        var c1 = SeedCampaign("One");
        _storylineCampaignRepo.Seed(storyline.Id, c1.Id);

        var result = await _service.SetStorylineCampaignsAsync(Command(storyline.Id, Array.Empty<Guid>()), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(await _storylineCampaignRepo.ListByArtifactIdAsync(storyline.Id, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task SetCampaigns_PlayerIsRejected()
    {
        var storyline = SeedArtifact("Arc");
        var c1 = SeedCampaign("One");

        var result = await _service.SetStorylineCampaignsAsync(
            Command(storyline.Id, new[] { c1.Id }, WorldRole.Player), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task SetCampaigns_NonStorylineArtifactIsNotFound()
    {
        var location = SeedArtifact("A place", ArtifactType.Location);
        var c1 = SeedCampaign("One");

        var result = await _service.SetStorylineCampaignsAsync(Command(location.Id, new[] { c1.Id }), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task SetCampaigns_CampaignFromAnotherWorldIsRejected()
    {
        var storyline = SeedArtifact("Arc");
        var foreign = SeedCampaign("Elsewhere", worldId: Guid.NewGuid());

        var result = await _service.SetStorylineCampaignsAsync(Command(storyline.Id, new[] { foreign.Id }), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_campaign"));
    }

    [Test]
    public async Task GetDetail_IncludesDeclaredCampaigns()
    {
        var storyline = SeedArtifact("Arc");
        var alpha = SeedCampaign("Alpha");
        var beta = SeedCampaign("Beta");
        _storylineCampaignRepo.Seed(storyline.Id, alpha.Id, beta.Id);

        var result = await _service.GetDetailAsync(storyline.Id, _worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.DeclaredCampaigns.Select(c => c.Name), Is.EquivalentTo(new[] { "Alpha", "Beta" }));
    }

    [Test]
    public async Task GetDetail_NonStorylineHasNoDeclaredCampaigns()
    {
        var location = SeedArtifact("A place", ArtifactType.Location);

        var result = await _service.GetDetailAsync(location.Id, _worldId, _gmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.DeclaredCampaigns, Is.Empty);
    }
}
