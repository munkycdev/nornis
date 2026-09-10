using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// A campaign with dates can offer to file the sources those dates say belong to it. Two
/// halves: what the page offers (only unfiled, only dated, only inside the span, only to a
/// GM) and what the confirmation accepts (only what is unfiled in this world right now).
///
/// The seeded dates are deliberately close to the edges: the whole feature is a comparison
/// against two bounds, and the defect it would ship is an off-by-a-day on the last night.
/// </summary>
[TestFixture]
public class CampaignFilesItsSourcesTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private static readonly Guid PlayerUserId = Guid.NewGuid();

    private static readonly DateTimeOffset Began = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ended = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private InMemoryCampaignRepository _campaigns = null!;
    private InMemorySourceRepository _sources = null!;
    private CampaignService _sut = null!;
    private Campaign _campaign = null!;

    [SetUp]
    public void SetUp()
    {
        _sources = new InMemorySourceRepository();
        var characters = new InMemoryCharacterRepository();
        _campaigns = new InMemoryCampaignRepository(_sources, characters);
        _sut = new CampaignService(_campaigns, characters, _sources, new InMemoryCampaignRecapRepository(),
            new InMemoryWorldRepository());

        _campaign = SeedCampaign("The Salt Road", Began, Ended);
    }

    private Campaign SeedCampaign(string name, DateTimeOffset? began, DateTimeOffset? ended, Guid? worldId = null)
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Name = name,
            Status = CampaignStatus.Active,
            StartedAt = began,
            EndedAt = ended,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId,
        };
        _campaigns.Seed(campaign);
        return campaign;
    }

    private Source SeedSource(string title, DateTimeOffset? occurredAt, Guid? campaignId = null, Guid? worldId = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            CampaignId = campaignId,
            Type = SourceType.SessionNote,
            Title = title,
            OccurredAt = occurredAt,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = PlayerUserId,
        };
        _sources.Seed(source);
        return source;
    }

    private async Task<UnfiledSources> OfferedTo(WorldRole role, Campaign? campaign = null)
    {
        campaign ??= _campaign;
        var result = await _sut.GetDetailAsync(campaign.Id, WorldId,
            role == WorldRole.GM ? GmUserId : PlayerUserId, role, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!.UnfiledInSpan;
    }

    private Task<Errors.AppResult<int>> FileAsync(WorldRole role, params Guid[] sourceIds) =>
        _sut.FileSourcesAsync(new FileCampaignSourcesCommand(_campaign.Id, WorldId,
            role == WorldRole.GM ? GmUserId : PlayerUserId, role, sourceIds), CancellationToken.None);

    #region The offer

    [Test]
    public async Task Offers_only_unfiled_dated_sources_inside_the_span()
    {
        var inside = SeedSource("Session 3", Began.AddDays(40));
        SeedSource("Before the campaign", Began.AddDays(-1));
        SeedSource("After the campaign", Ended.AddDays(2));
        SeedSource("Undated lore", occurredAt: null);
        var other = SeedCampaign("The Side Game", Began, Ended);
        SeedSource("Filed under the side game", Began.AddDays(40), campaignId: other.Id);
        SeedSource("Already here", Began.AddDays(41), campaignId: _campaign.Id);

        var offered = await OfferedTo(WorldRole.GM);

        Assert.Multiple(() =>
        {
            Assert.That(offered.Items.Select(s => s.Id), Is.EqualTo([inside.Id]));
            Assert.That(offered.TotalCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task The_first_and_last_days_are_inside()
    {
        // The end date is a day, not a midnight: a session played that evening belongs.
        var firstMorning = SeedSource("First session", Began);
        var lastEvening = SeedSource("Last session", Ended.AddHours(19).AddMinutes(30));
        SeedSource("The morning after", Ended.AddDays(1));

        var offered = await OfferedTo(WorldRole.GM);

        Assert.That(offered.Items.Select(s => s.Id), Is.EquivalentTo([firstMorning.Id, lastEvening.Id]));
    }

    [Test]
    public async Task A_campaign_with_only_a_start_is_open_ended()
    {
        var running = SeedCampaign("Still running", Began, ended: null);
        var later = SeedSource("Years later", Began.AddYears(3));
        SeedSource("Before it began", Began.AddDays(-1));

        var offered = await OfferedTo(WorldRole.GM, running);

        Assert.That(offered.Items.Select(s => s.Id), Is.EqualTo([later.Id]));
    }

    [Test]
    public async Task A_campaign_with_only_an_end_reaches_back_indefinitely()
    {
        var finished = SeedCampaign("The prequel", began: null, Ended);
        var longAgo = SeedSource("The founding", Ended.AddYears(-10));
        SeedSource("After it ended", Ended.AddDays(1));

        var offered = await OfferedTo(WorldRole.GM, finished);

        Assert.That(offered.Items.Select(s => s.Id), Is.EqualTo([longAgo.Id]));
    }

    [Test]
    public async Task An_undated_campaign_offers_nothing()
    {
        var undated = SeedCampaign("No dates yet", began: null, ended: null);
        SeedSource("Session 1", Began.AddDays(1));

        var offered = await OfferedTo(WorldRole.GM, undated);

        Assert.That(offered, Is.EqualTo(UnfiledSources.None));
    }

    [Test]
    public async Task A_player_is_offered_nothing()
    {
        // Only a GM can act on it, so only a GM is told; a player's page carries no trace.
        SeedSource("Session 3", Began.AddDays(40));

        var toGm = await OfferedTo(WorldRole.GM);
        var toPlayer = await OfferedTo(WorldRole.Player);

        Assert.Multiple(() =>
        {
            Assert.That(toGm.TotalCount, Is.EqualTo(1), "the GM should have been offered it");
            Assert.That(toPlayer, Is.EqualTo(UnfiledSources.None));
        });
    }

    [Test]
    public async Task The_offer_is_capped_and_says_how_many_there_are()
    {
        for (var i = 0; i < CampaignService.MaxUnfiledOffered + 3; i++)
        {
            SeedSource($"Session {i}", Began.AddDays(i));
        }

        var offered = await OfferedTo(WorldRole.GM);

        Assert.Multiple(() =>
        {
            Assert.That(offered.Items, Has.Count.EqualTo(CampaignService.MaxUnfiledOffered));
            Assert.That(offered.TotalCount, Is.EqualTo(CampaignService.MaxUnfiledOffered + 3));
        });
    }

    #endregion

    #region The confirmation

    [Test]
    public async Task Files_exactly_the_chosen_sources()
    {
        var chosen = SeedSource("Session 3", Began.AddDays(40));
        var unchosen = SeedSource("Session 4", Began.AddDays(47));

        var result = await FileAsync(WorldRole.GM, chosen.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value, Is.EqualTo(1));
            Assert.That(chosen.CampaignId, Is.EqualTo(_campaign.Id));
            Assert.That(unchosen.CampaignId, Is.Null, "an unchecked source was filed");
        });
    }

    [Test]
    public async Task Filed_sources_leave_the_offer()
    {
        var source = SeedSource("Session 3", Began.AddDays(40));

        await FileAsync(WorldRole.GM, source.Id);
        var offered = await OfferedTo(WorldRole.GM);

        Assert.That(offered.TotalCount, Is.Zero);
    }

    [Test]
    public async Task Is_refused_to_a_player()
    {
        var source = SeedSource("Session 3", Began.AddDays(40));

        var result = await FileAsync(WorldRole.Player, source.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(source.CampaignId, Is.Null);
        });
    }

    [Test]
    public async Task Refuses_a_campaign_from_another_world()
    {
        var foreign = SeedCampaign("Elsewhere", Began, Ended, worldId: Guid.NewGuid());
        var source = SeedSource("Session 3", Began.AddDays(40));

        var result = await _sut.FileSourcesAsync(
            new FileCampaignSourcesCommand(foreign.Id, WorldId, GmUserId, WorldRole.GM, [source.Id]), CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Refuses_a_source_already_filed_elsewhere_and_moves_nothing()
    {
        // The offer never lists these; a stale page or a hand-built request could still
        // name one, and the answer is a refusal of the whole request, not a partial move.
        var other = SeedCampaign("The Side Game", Began, Ended);
        var theirs = SeedSource("Their session", Began.AddDays(40), campaignId: other.Id);
        var free = SeedSource("Session 3", Began.AddDays(41));

        var result = await FileAsync(WorldRole.GM, free.Id, theirs.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(theirs.CampaignId, Is.EqualTo(other.Id));
            Assert.That(free.CampaignId, Is.Null, "part of a refused request was applied");
        });
    }

    [Test]
    public async Task Refuses_a_source_from_another_world()
    {
        var foreign = SeedSource("Another world's session", Began.AddDays(40), worldId: Guid.NewGuid());

        var result = await FileAsync(WorldRole.GM, foreign.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(foreign.CampaignId, Is.Null);
        });
    }

    [Test]
    public async Task Refuses_an_empty_selection()
    {
        var result = await FileAsync(WorldRole.GM);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Accepts_a_source_the_dates_do_not_cover()
    {
        // The span is how the offer is built, not a rule on the write: a GM who knows a
        // session belongs here may file it whatever its date says.
        var early = SeedSource("Session zero", Began.AddDays(-30));

        var result = await FileAsync(WorldRole.GM, early.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(early.CampaignId, Is.EqualTo(_campaign.Id));
        });
    }

    #endregion
}
