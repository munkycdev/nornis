using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The world's current campaign — the run of play a capture belongs to unless the GM says
/// otherwise. This service is its only writer, so its four rules are pinned here: who may
/// choose it, that whatever it names is Active, that a world adopts its first campaign
/// without being asked, and that a campaign leaving Active stops being current.
/// </summary>
[TestFixture]
public class CampaignServiceCurrentTests
{
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryCharacterRepository _characterRepository = null!;
    private InMemoryCampaignRepository _campaignRepository = null!;
    private InMemoryCampaignRecapRepository _recapRepository = null!;
    private InMemoryWorldRepository _worldRepository = null!;
    private CampaignService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _characterRepository = new InMemoryCharacterRepository();
        _campaignRepository = new InMemoryCampaignRepository(_sourceRepository, _characterRepository);
        _recapRepository = new InMemoryCampaignRecapRepository();
        _worldRepository = new InMemoryWorldRepository();
        _sut = new CampaignService(
            _campaignRepository, _characterRepository, _sourceRepository, _recapRepository,
            _worldRepository);

        _worldRepository.CreateAsync(new World
        {
            Id = WorldId,
            Name = "Ruins of Symbaroum",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId
        }).GetAwaiter().GetResult();
    }

    private Guid? CurrentCampaignId =>
        _worldRepository.Worlds.Single(w => w.Id == WorldId).CurrentCampaignId;

    private async Task<Campaign> AddCampaignAsync(
        string name, CampaignStatus status = CampaignStatus.Active)
    {
        var result = await _sut.CreateAsync(
            new CreateCampaignCommand(WorldId, name, GmUserId, WorldRole.GM, Status: status),
            CancellationToken.None);

        return result.Value!;
    }

    #region Adopting without being asked

    [Test]
    public async Task AWorldsFirstActiveCampaign_BecomesCurrentWithoutBeingAsked()
    {
        // Almost every world runs one campaign at a time. If those GMs had to go and declare
        // the obvious, the common case would keep filing sessions under no campaign — which is
        // the state this pointer exists to stop being the default.
        var campaign = await AddCampaignAsync("Missing Caravan Arc");

        Assert.That(CurrentCampaignId, Is.EqualTo(campaign.Id));
    }

    [Test]
    public async Task ASecondCampaign_DoesNotStealCurrentFromTheFirst()
    {
        var first = await AddCampaignAsync("Missing Caravan Arc");
        await AddCampaignAsync("Black Harbor Nights");

        Assert.That(CurrentCampaignId, Is.EqualTo(first.Id),
            "adding a campaign is not the same as switching to it");
    }

    [Test]
    public async Task ACompletedCampaign_IsNeverAdopted()
    {
        await AddCampaignAsync("The Silver Key", CampaignStatus.Completed);

        Assert.That(CurrentCampaignId, Is.Null, "a finished campaign is not the run in progress");
    }

    #endregion

    #region Choosing one

    [Test]
    public async Task AGmCanSwitchTheCurrentCampaign()
    {
        await AddCampaignAsync("Missing Caravan Arc");
        var second = await AddCampaignAsync("Black Harbor Nights");

        var result = await _sut.SetCurrentAsync(second.Id, WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(CurrentCampaignId, Is.EqualTo(second.Id));
    }

    [Test]
    public async Task APlayerCannotChooseTheCurrentCampaign()
    {
        var first = await AddCampaignAsync("Missing Caravan Arc");
        var second = await AddCampaignAsync("Black Harbor Nights");

        var result = await _sut.SetCurrentAsync(second.Id, WorldId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(CurrentCampaignId, Is.EqualTo(first.Id), "unchanged");
    }

    [Test]
    public async Task ACampaignThatIsNotActive_CannotBeMadeCurrent()
    {
        // The pointer's one invariant. Refusing here is the only reason readers never have to
        // check the status of the campaign it hands them.
        var active = await AddCampaignAsync("Missing Caravan Arc");
        var finished = await AddCampaignAsync("The Silver Key", CampaignStatus.Completed);

        var result = await _sut.SetCurrentAsync(finished.Id, WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("campaign_not_active"));
        Assert.That(CurrentCampaignId, Is.EqualTo(active.Id));
    }

    [Test]
    public async Task ACampaignInAnotherWorld_CannotBeMadeCurrent()
    {
        var elsewhere = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            Name = "Someone else's game",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId
        };
        await _campaignRepository.CreateAsync(elsewhere, CancellationToken.None);

        var result = await _sut.SetCurrentAsync(elsewhere.Id, WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(CurrentCampaignId, Is.Null);
    }

    [Test]
    public async Task AGmCanClearIt_LeavingCapturesToDefaultToNoCampaign()
    {
        await AddCampaignAsync("Missing Caravan Arc");

        var result = await _sut.ClearCurrentAsync(WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(CurrentCampaignId, Is.Null);
    }

    [Test]
    public async Task APlayerCannotClearIt()
    {
        var campaign = await AddCampaignAsync("Missing Caravan Arc");

        var result = await _sut.ClearCurrentAsync(WorldId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(CurrentCampaignId, Is.EqualTo(campaign.Id));
    }

    #endregion

    #region Staying honest

    [Test]
    public async Task CompletingTheCurrentCampaign_ClearsIt()
    {
        // Otherwise the pointer would name a finished campaign and every reader would have to
        // second-guess the status of what it handed them.
        var campaign = await AddCampaignAsync("Missing Caravan Arc");

        await _sut.UpdateAsync(
            new UpdateCampaignCommand(campaign.Id, WorldId, GmUserId, WorldRole.GM,
                Status: CampaignStatus.Completed),
            CancellationToken.None);

        Assert.That(CurrentCampaignId, Is.Null);
    }

    [Test]
    public async Task CompletingSomeOtherCampaign_LeavesTheCurrentOneAlone()
    {
        var current = await AddCampaignAsync("Missing Caravan Arc");
        var other = await AddCampaignAsync("Black Harbor Nights");

        await _sut.UpdateAsync(
            new UpdateCampaignCommand(other.Id, WorldId, GmUserId, WorldRole.GM,
                Status: CampaignStatus.Archived),
            CancellationToken.None);

        Assert.That(CurrentCampaignId, Is.EqualTo(current.Id));
    }

    [Test]
    public async Task RenamingTheCurrentCampaign_DoesNotDisturbIt()
    {
        var campaign = await AddCampaignAsync("Missing Caravan Arc");

        await _sut.UpdateAsync(
            new UpdateCampaignCommand(campaign.Id, WorldId, GmUserId, WorldRole.GM,
                Name: "The Missing Caravan"),
            CancellationToken.None);

        Assert.That(CurrentCampaignId, Is.EqualTo(campaign.Id));
    }

    #endregion
}
