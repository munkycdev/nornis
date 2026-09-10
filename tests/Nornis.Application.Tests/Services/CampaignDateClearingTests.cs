using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// A campaign's dates could be set but never removed: the update treated null as "leave it",
/// so the only way to un-date a campaign was to delete it. Clearing is now said explicitly,
/// and null still leaves a date alone — the two meanings have two values.
/// </summary>
[TestFixture]
public class CampaignDateClearingTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryCampaignRepository _campaigns = null!;
    private CampaignService _sut = null!;
    private Campaign _campaign = null!;

    [SetUp]
    public async Task SetUp()
    {
        _campaigns = new InMemoryCampaignRepository();
        _sut = new CampaignService(_campaigns, new InMemoryCharacterRepository(), new InMemorySourceRepository(),
            new InMemoryCampaignRecapRepository(), new InMemoryWorldRepository());

        var now = DateTimeOffset.UtcNow;
        _campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = "The Bleeding Heart",
            Status = CampaignStatus.Active,
            StartedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = GmUserId,
        };
        await _campaigns.CreateAsync(_campaign);
    }

    [Test]
    public async Task Null_LeavesADateAlone()
    {
        var result = await _sut.UpdateAsync(
            new UpdateCampaignCommand(_campaign.Id, WorldId, GmUserId, WorldRole.GM, Name: "Renamed"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.StartedAt, Is.EqualTo(_campaign.StartedAt));
            Assert.That(result.Value.EndedAt, Is.EqualTo(_campaign.EndedAt));
        });
    }

    [Test]
    public async Task Clear_RemovesTheDateItNames()
    {
        var result = await _sut.UpdateAsync(
            new UpdateCampaignCommand(_campaign.Id, WorldId, GmUserId, WorldRole.GM, ClearEndedAt: true), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.StartedAt, Is.EqualTo(_campaign.StartedAt), "the other date is untouched");
            Assert.That(result.Value.EndedAt, Is.Null);
        });
    }

    [Test]
    public async Task Clear_WinsOverAValueInTheSameRequest()
    {
        var result = await _sut.UpdateAsync(
            new UpdateCampaignCommand(_campaign.Id, WorldId, GmUserId, WorldRole.GM,
                StartedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), ClearStartedAt: true), CancellationToken.None);

        Assert.That(result.Value!.StartedAt, Is.Null, "an explicit clear is the stronger statement");
    }
}
