using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Campaign-context behavior of <see cref="SourceService"/>: declaring, changing,
/// clearing, and filtering by the source's campaign.
/// </summary>
[TestFixture]
public class SourceServiceCampaignTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryCampaignRepository _campaignRepository = null!;
    private SourceService _sut = null!;
    private Campaign _campaign = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _campaignRepository = new InMemoryCampaignRepository();
        _sut = new SourceService(_sourceRepository, new InMemoryWorldMemberRepository(), _campaignRepository, new FakeExtractionQueueClient());

        _campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = "Rise of Tiamat",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = UserId
        };
        _campaignRepository.Seed(_campaign);
    }

    private CreateSourceCommand CreateCommand(Guid? campaignId = null) => new(
        WorldId, "Session 1", SourceType.SessionNote, VisibilityScope.PartyVisible,
        UserId, WorldRole.GM, Body: "We met Captain Voss.", CampaignId: campaignId);

    [Test]
    public async Task CreateAsync_WithCampaign_StoresCampaignId()
    {
        var result = await _sut.CreateAsync(CreateCommand(_campaign.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CampaignId, Is.EqualTo(_campaign.Id));
    }

    [Test]
    public async Task CreateAsync_WithoutCampaign_StoresNull()
    {
        var result = await _sut.CreateAsync(CreateCommand(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CampaignId, Is.Null);
    }

    [Test]
    public async Task CreateAsync_UnknownCampaign_Returns400()
    {
        var result = await _sut.CreateAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_campaign"));
    }

    [Test]
    public async Task CreateAsync_CampaignFromAnotherWorld_Returns400()
    {
        var foreign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            Name = "Someone else's game",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = UserId
        };
        _campaignRepository.Seed(foreign);

        var result = await _sut.CreateAsync(CreateCommand(foreign.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_campaign"));
    }

    [Test]
    public async Task UpdateAsync_SetsCampaign()
    {
        var created = await _sut.CreateAsync(CreateCommand(), CancellationToken.None);

        var command = new UpdateSourceCommand(created.Value!.Id, WorldId, UserId, WorldRole.GM, CampaignId: _campaign.Id);
        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CampaignId, Is.EqualTo(_campaign.Id));
    }

    [Test]
    public async Task UpdateAsync_ClearCampaign_RemovesCampaign()
    {
        var created = await _sut.CreateAsync(CreateCommand(_campaign.Id), CancellationToken.None);

        var command = new UpdateSourceCommand(created.Value!.Id, WorldId, UserId, WorldRole.GM, ClearCampaign: true);
        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CampaignId, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_NoCampaignFields_LeavesCampaignUnchanged()
    {
        var created = await _sut.CreateAsync(CreateCommand(_campaign.Id), CancellationToken.None);

        var command = new UpdateSourceCommand(created.Value!.Id, WorldId, UserId, WorldRole.GM, Title: "Session 1 (edited)");
        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CampaignId, Is.EqualTo(_campaign.Id));
    }

    [Test]
    public async Task UpdateAsync_InvalidCampaign_Returns400()
    {
        var created = await _sut.CreateAsync(CreateCommand(), CancellationToken.None);

        var command = new UpdateSourceCommand(created.Value!.Id, WorldId, UserId, WorldRole.GM, CampaignId: Guid.NewGuid());
        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_campaign"));
    }

    [Test]
    public async Task ListByWorldAsync_FiltersByCampaign()
    {
        await _sut.CreateAsync(CreateCommand(_campaign.Id), CancellationToken.None);
        await _sut.CreateAsync(CreateCommand(), CancellationToken.None);

        var byCampaign = await _sut.ListByWorldAsync(WorldId, UserId, WorldRole.GM, CancellationToken.None,
            campaignId: _campaign.Id);
        var unassigned = await _sut.ListByWorldAsync(WorldId, UserId, WorldRole.GM, CancellationToken.None,
            unassignedOnly: true);
        var all = await _sut.ListByWorldAsync(WorldId, UserId, WorldRole.GM, CancellationToken.None);

        Assert.That(byCampaign.Value, Has.Count.EqualTo(1));
        Assert.That(byCampaign.Value![0].CampaignId, Is.EqualTo(_campaign.Id));
        Assert.That(unassigned.Value, Has.Count.EqualTo(1));
        Assert.That(unassigned.Value![0].CampaignId, Is.Null);
        Assert.That(all.Value, Has.Count.EqualTo(2));
    }
}
