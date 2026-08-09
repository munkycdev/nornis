using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The campaign page's read model and the reorder write.
///
/// The rollup itself is SQL and is proven in <c>CampaignRollupRepositoryTests</c>; what
/// matters here is that the service hands the repository the <em>caller's</em> filter rather
/// than reading as a GM and trimming afterwards. Everything downstream of that choice is
/// only as narrow as the filter it was given.
/// </summary>
[TestFixture]
public class CampaignServiceDetailTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private static readonly Guid PlayerUserId = Guid.NewGuid();

    private InMemoryCampaignRepository _campaignRepository = null!;
    private InMemoryCharacterRepository _characterRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryCampaignRecapRepository _recapRepository = null!;
    private CampaignService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _characterRepository = new InMemoryCharacterRepository();
        _campaignRepository = new InMemoryCampaignRepository(_sourceRepository, _characterRepository);
        _recapRepository = new InMemoryCampaignRecapRepository();
        _sut = new CampaignService(_campaignRepository, _characterRepository, _sourceRepository, _recapRepository);
    }

    private Campaign SeedCampaign(Guid? worldId = null, string name = "The Missing Caravan")
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Name = name,
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId
        };
        _campaignRepository.Seed(campaign);
        return campaign;
    }

    #region Detail

    [Test]
    public async Task Detail_asks_the_rollup_for_the_callers_view_not_the_GMs()
    {
        var campaign = SeedCampaign();

        await _sut.GetDetailAsync(campaign.Id, WorldId, PlayerUserId, WorldRole.Player, CancellationToken.None);

        var filter = _campaignRepository.LastRollupFilter;

        Assert.Multiple(() =>
        {
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter!.Scopes, Does.Not.Contain(VisibilityScope.GMOnly),
                "the rollup ran with GMOnly in scope for a player");
            Assert.That(filter.PrivateOwnerUserId, Is.EqualTo(PlayerUserId),
                "the rollup would have returned another user's Private rows");
        });
    }

    [Test]
    public async Task Detail_gives_a_GM_the_unrestricted_filter()
    {
        var campaign = SeedCampaign();

        await _sut.GetDetailAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(_campaignRepository.LastRollupFilter!.Scopes, Contains.Item(VisibilityScope.GMOnly));
    }

    [Test]
    public async Task Detail_refuses_a_campaign_from_another_world()
    {
        var otherWorldCampaign = SeedCampaign(worldId: Guid.NewGuid());

        var result = await _sut.GetDetailAsync(
            otherWorldCampaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        });
    }

    [Test]
    public async Task Detail_is_readable_by_a_player()
    {
        // A campaign is not an authorization boundary; players get the page, narrowed.
        var campaign = SeedCampaign();

        var result = await _sut.GetDetailAsync(
            campaign.Id, WorldId, PlayerUserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task Detail_hides_the_GM_recap_from_a_player_and_shows_them_the_party_one()
    {
        var campaign = SeedCampaign();
        _recapRepository.Seed(new CampaignRecap
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            GmContentMarkdown = "The Harbourmaster is the traitor.",
            PartyContentMarkdown = "The caravan is still missing.",
            Model = "gpt-4o",
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratedByUserId = GmUserId
        });

        var asPlayer = await _sut.GetDetailAsync(
            campaign.Id, WorldId, PlayerUserId, WorldRole.Player, CancellationToken.None);
        var asGm = await _sut.GetDetailAsync(
            campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(asPlayer.Value!.Recap.Content, Is.EqualTo("The caravan is still missing."));
            Assert.That(asPlayer.Value.Recap.PartyPreview, Is.Null,
                "the player's copy carried a second rendering they should not know exists");
            Assert.That(asGm.Value!.Recap.Content, Is.EqualTo("The Harbourmaster is the traitor."));
            Assert.That(asGm.Value.Recap.PartyPreview, Is.EqualTo("The caravan is still missing."));
        });
    }

    [Test]
    public async Task Detail_reports_no_recap_before_one_is_generated()
    {
        var campaign = SeedCampaign();

        var result = await _sut.GetDetailAsync(
            campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Recap.HasData, Is.False);
            Assert.That(result.Value.Recap.Content, Is.Null);
        });
    }

    #endregion

    #region Reorder

    [Test]
    public async Task Reorder_writes_one_based_positions_in_the_order_given()
    {
        var first = SeedCampaign(name: "The Original Campaign");
        var second = SeedCampaign(name: "The Sequel");
        var third = SeedCampaign(name: "The Side Game");

        var result = await _sut.ReorderAsync(
            WorldId, [third.Id, first.Id, second.Id], WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Select(c => c.Name),
                Is.EqualTo(["The Side Game", "The Original Campaign", "The Sequel"]));
            Assert.That(result.Value!.Select(c => c.SortOrder), Is.EqualTo([1, 2, 3]));
        });
    }

    [Test]
    public async Task Reorder_trails_a_campaign_the_client_did_not_know_about()
    {
        var known = SeedCampaign(name: "The Original Campaign");
        SeedCampaign(name: "Created While The Page Was Open");

        var result = await _sut.ReorderAsync(WorldId, [known.Id], WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Select(c => c.Name),
                Is.EqualTo(["The Original Campaign", "Created While The Page Was Open"]));
            Assert.That(result.Value!.Select(c => c.SortOrder), Is.EqualTo([1, 2]),
                "an omitted campaign kept a zero and would sort ahead of the positioned ones");
        });
    }

    [Test]
    public async Task Reorder_is_refused_to_a_player()
    {
        var campaign = SeedCampaign();

        var result = await _sut.ReorderAsync(WorldId, [campaign.Id], WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task Reorder_is_refused_to_an_observer()
    {
        var campaign = SeedCampaign();

        var result = await _sut.ReorderAsync(WorldId, [campaign.Id], WorldRole.Observer, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Reorder_refuses_a_campaign_from_another_world()
    {
        SeedCampaign();
        var foreign = SeedCampaign(worldId: Guid.NewGuid(), name: "Another World's Campaign");

        var result = await _sut.ReorderAsync(WorldId, [foreign.Id], WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("invalid_campaign"));
        });
    }

    [Test]
    public async Task Reorder_refuses_a_repeated_campaign()
    {
        var campaign = SeedCampaign();

        var result = await _sut.ReorderAsync(
            WorldId, [campaign.Id, campaign.Id], WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
        });
    }

    #endregion
}
