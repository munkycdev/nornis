using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// The learned endpoint end-to-end. Unlike every other world surface, this one is *for* the
/// unprivileged: a Player and an Observer must both reach it and see the same thing, and
/// neither may see a trace of what has not been disclosed.
/// </summary>
[TestFixture]
[Category("Feature: what-you-learned")]
public class LearnedEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private string Url => $"/api/worlds/{_scenario.World.Id}/learned";

    private async Task<Guid> SeedRevealAsync(string? note, bool alsoSeedASecret = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Name = "Captain Voss",
            Type = ArtifactType.Character,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = now.AddDays(-100),
            UpdatedAt = now
        };

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Type = SourceType.Reveal,
            Title = "Reveal — test",
            Body = "Revealed to the party:\n- Character: Captain Voss",
            RevealNote = note,
            OccurredAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
            CreatedByUserId = _scenario.GmUserId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Kind = ReviewBatchKinds.Reveal,
            Status = ReviewBatchStatus.Completed,
            CreatedAt = now.AddDays(-1)
        };

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.UpdateArtifact,
            TargetType = ReviewTargetType.Artifact,
            TargetId = artifact.Id,
            ProposedValueJson = "{}",
            Rationale = "Revealed to the party.",
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = now.AddDays(-1)
        };

        db.Artifacts.Add(artifact);
        db.Sources.Add(source);
        db.ReviewBatches.Add(batch);
        db.ReviewProposals.Add(proposal);

        if (alsoSeedASecret)
        {
            db.Artifacts.Add(new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = _scenario.World.Id,
                Name = "The thing they must not know",
                Type = ArtifactType.Concept,
                Visibility = VisibilityScope.GMOnly,
                Status = ArtifactStatus.Active,
                CreatedAt = now.AddDays(-100),
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        return source.Id;
    }

    #region Authorization

    [Test]
    [Category("Authorization")]
    public async Task Get_NonMember_DoesNotRevealTheWorldExists()
    {
        var response = await _scenario.GmClient.GetAsync($"/api/worlds/{Guid.NewGuid()}/learned");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    [Category("Authorization")]
    public async Task Get_AsPlayerAndObserver_BothSeeTheSameDisclosures()
    {
        await SeedRevealAsync("The letter names him.");

        var asPlayer = await _scenario.PlayerClient.GetFromJsonAsync<LearnedResponse>(Url);
        var asObserver = await _scenario.ObserverClient.GetFromJsonAsync<LearnedResponse>(Url);

        // Both read at the party floor, so both learned the same thing.
        Assert.Multiple(() =>
        {
            Assert.That(asPlayer!.Entries, Has.Count.EqualTo(1));
            Assert.That(asObserver!.Entries.Select(e => e.SourceId),
                Is.EqualTo(asPlayer.Entries.Select(e => e.SourceId)).AsCollection);
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task GmOnlyMaterial_LeavesNoTraceInThePlayersView()
    {
        await SeedRevealAsync("The letter names him.", alsoSeedASecret: true);

        var payload = await _scenario.PlayerClient.GetStringAsync(Url);

        Assert.That(payload, Does.Not.Contain("The thing they must not know"));
    }

    #endregion

    #region Reading

    [Test]
    public async Task Get_ReturnsTheDisclosureWithTheGmsOwnWords()
    {
        var sourceId = await SeedRevealAsync("The letter you found names the harbourmaster.");

        var digest = await _scenario.PlayerClient.GetFromJsonAsync<LearnedResponse>(Url);

        Assert.Multiple(() =>
        {
            Assert.That(digest!.Entries.Single().SourceId, Is.EqualTo(sourceId));
            Assert.That(digest.Entries.Single().GmNote,
                Is.EqualTo("The letter you found names the harbourmaster."));
            Assert.That(digest.Entries.Single().Elements.Select(e => e.Name), Does.Contain("Captain Voss"));
            Assert.That(digest.SeenThrough, Is.Null, "this reader has never looked");
        });
    }

    [Test]
    public async Task Get_WithNothingDisclosed_Returns200AndAnEmptyList()
    {
        var digest = await _scenario.PlayerClient.GetFromJsonAsync<LearnedResponse>(Url);

        Assert.Multiple(() =>
        {
            Assert.That(digest!.Entries, Is.Empty);
            Assert.That(digest.HasMore, Is.False);
        });
    }

    #endregion

    #region Marking seen

    [Test]
    public async Task MarkSeen_ThenReading_ReturnsNothingNew()
    {
        await SeedRevealAsync("The letter names him.");

        var seen = await _scenario.PlayerClient.PostAsJsonAsync(
            $"{Url}/seen", new { seenThrough = DateTimeOffset.UtcNow });
        Assert.That(seen.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await _scenario.PlayerClient.GetFromJsonAsync<LearnedResponse>(Url);

        Assert.Multiple(() =>
        {
            Assert.That(after!.Entries, Is.Empty);
            Assert.That(after.SeenThrough, Is.Not.Null);
        });
    }

    [Test]
    public async Task MarkSeen_IsIdempotent()
    {
        await SeedRevealAsync("The letter names him.");
        var point = DateTimeOffset.UtcNow;

        await _scenario.PlayerClient.PostAsJsonAsync($"{Url}/seen", new { seenThrough = point });
        var second = await _scenario.PlayerClient.PostAsJsonAsync($"{Url}/seen", new { seenThrough = point });

        // A second tab is not a conflict.
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    [Category("Authorization")]
    public async Task MarkSeen_ByOneMember_LeavesAnothersViewUntouched()
    {
        await SeedRevealAsync("The letter names him.");

        await _scenario.PlayerClient.PostAsJsonAsync(
            $"{Url}/seen", new { seenThrough = DateTimeOffset.UtcNow });

        var observerView = await _scenario.ObserverClient.GetFromJsonAsync<LearnedResponse>(Url);

        Assert.That(observerView!.Entries, Has.Count.EqualTo(1),
            "one reader marking their place must not close the list for anyone else");
    }

    #endregion
}
