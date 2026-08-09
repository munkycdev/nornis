using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// <c>CampaignRepository.GetRollupAsync</c> — the derived "what this campaign touched" query.
///
/// The visibility cases are the point. The rollup joins four tables that each carry their own
/// scope, and a campaign page that names an artifact its reader was refused elsewhere is a
/// leak through a side door: the Codex hides Captain Voss, and the campaign page lists him in
/// the cast anyway. Each of the four is exercised separately below, because omitting any one
/// of them still returns a plausible-looking list.
/// </summary>
[TestFixture]
public class CampaignRollupRepositoryTests : IntegrationTestBase
{
    private CampaignRepository _sut = null!;
    private World _world = null!;
    private User _gm = null!;
    private User _player = null!;
    private Campaign _campaign = null!;

    /// <summary>What a Player who owns nothing Private may see.</summary>
    private VisibilityFilter PlayerFilter => VisibilityFilter.ForRole(WorldRole.Player, _player.Id);

    [SetUp]
    public void SetUp()
    {
        _sut = new CampaignRepository(Context);

        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        _gm = SeedUser($"gm-{tag}");
        _player = SeedUser($"player-{tag}");

        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = _gm.Id,
            RowVersion = []
        };
        Context.Worlds.Add(_world);

        _campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Name = "The Missing Caravan",
            Status = CampaignStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = _gm.Id
        };
        Context.Campaigns.Add(_campaign);
        Context.SaveChanges();
    }

    #region Seeding

    private User SeedUser(string tag)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = tag,
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        Context.Users.Add(user);
        return user;
    }

    private Source SeedSource(
        string title,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        Guid? campaignId = null,
        bool inCampaign = true)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            CampaignId = inCampaign ? campaignId ?? _campaign.Id : null,
            Type = SourceType.SessionNote,
            Title = title,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = _gm.Id
        };
        Context.Sources.Add(source);
        Context.SaveChanges();
        return source;
    }

    private Artifact SeedArtifact(
        string name,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        ArtifactType type = ArtifactType.Character)
    {
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Type = type,
            Name = name,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = _gm.Id,
            RowVersion = []
        };
        Context.Artifacts.Add(artifact);
        Context.SaveChanges();
        return artifact;
    }

    private ArtifactFact SeedFact(Artifact artifact, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var now = DateTimeOffset.UtcNow;
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "location",
            Value = "Black Harbor",
            TruthState = TruthState.Confirmed,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = _gm.Id,
            RowVersion = []
        };
        Context.ArtifactFacts.Add(fact);
        Context.SaveChanges();
        return fact;
    }

    private ArtifactRelationship SeedRelationship(
        Artifact a, Artifact b, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        var now = DateTimeOffset.UtcNow;
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            ArtifactAId = a.Id,
            ArtifactBId = b.Id,
            Type = "SuspectedIn",
            TruthState = TruthState.Likely,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = _gm.Id,
            RowVersion = []
        };
        Context.ArtifactRelationships.Add(relationship);
        Context.SaveChanges();
        return relationship;
    }

    private void SeedReference(Source source, SourceReferenceTargetType targetType, Guid targetId)
    {
        Context.SourceReferences.Add(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        Context.SaveChanges();
    }

    #endregion

    #region Citation routes

    [Test]
    public async Task Rollup_includes_an_artifact_cited_directly()
    {
        var voss = SeedArtifact("Captain Voss");
        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.Artifact, voss.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.That(rollup.Artifacts.Select(a => a.Name), Is.EqualTo(["Captain Voss"]));
    }

    [Test]
    public async Task Rollup_includes_an_artifact_cited_through_one_of_its_facts()
    {
        var voss = SeedArtifact("Captain Voss");
        var fact = SeedFact(voss);
        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.ArtifactFact, fact.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.That(rollup.Artifacts.Select(a => a.Name), Is.EqualTo(["Captain Voss"]));
    }

    [Test]
    public async Task Rollup_includes_both_ends_of_a_cited_relationship()
    {
        var voss = SeedArtifact("Captain Voss");
        var caravan = SeedArtifact("Missing Caravan", type: ArtifactType.Event);
        var relationship = SeedRelationship(voss, caravan);
        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.ArtifactRelationship, relationship.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.That(rollup.Artifacts.Select(a => a.Name),
            Is.EquivalentTo(["Captain Voss", "Missing Caravan"]));
    }

    #endregion

    #region Ranking

    [Test]
    public async Task Rollup_ranks_by_how_many_sources_cite_each_artifact()
    {
        var voss = SeedArtifact("Captain Voss");
        var key = SeedArtifact("Silver Key", type: ArtifactType.Item);

        foreach (var title in new[] { "Session 1", "Session 2", "Session 3" })
        {
            SeedReference(SeedSource(title), SourceReferenceTargetType.Artifact, voss.Id);
        }

        SeedReference(SeedSource("Session 4"), SourceReferenceTargetType.Artifact, key.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.Multiple(() =>
        {
            Assert.That(rollup.Artifacts.Select(a => a.Name), Is.EqualTo(["Captain Voss", "Silver Key"]));
            Assert.That(rollup.Artifacts[0].SourceCount, Is.EqualTo(3));
            Assert.That(rollup.Artifacts[1].SourceCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task One_source_citing_an_artifact_two_ways_counts_once()
    {
        var voss = SeedArtifact("Captain Voss");
        var fact = SeedFact(voss);
        var session = SeedSource("Session 1");

        // The same session names the artifact and one of its facts — one session's worth of
        // evidence, however many rows record it.
        SeedReference(session, SourceReferenceTargetType.Artifact, voss.Id);
        SeedReference(session, SourceReferenceTargetType.ArtifactFact, fact.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.That(rollup.Artifacts.Single().SourceCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Rollup_reports_the_total_it_was_cut_from()
    {
        for (var i = 0; i < 5; i++)
        {
            var artifact = SeedArtifact($"Artifact {i}");
            SeedReference(SeedSource($"Session {i}"), SourceReferenceTargetType.Artifact, artifact.Id);
        }

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, limit: 2);

        Assert.Multiple(() =>
        {
            Assert.That(rollup.Artifacts, Has.Count.EqualTo(2));
            Assert.That(rollup.TotalCount, Is.EqualTo(5));
        });
    }

    #endregion

    #region Scope

    [Test]
    public async Task Sources_outside_the_campaign_do_not_contribute()
    {
        var voss = SeedArtifact("Captain Voss");
        SeedReference(SeedSource("Worldbuilding note", inCampaign: false),
            SourceReferenceTargetType.Artifact, voss.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.That(rollup.Artifacts, Is.Empty);
    }

    [Test]
    public async Task Sources_in_a_different_campaign_do_not_contribute()
    {
        var other = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Name = "The Sequel",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _gm.Id
        };
        Context.Campaigns.Add(other);
        Context.SaveChanges();

        var voss = SeedArtifact("Captain Voss");
        SeedReference(SeedSource("Sequel session", campaignId: other.Id),
            SourceReferenceTargetType.Artifact, voss.Id);

        var rollup = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);

        Assert.That(rollup.Artifacts, Is.Empty);
    }

    #endregion

    #region Visibility — one case per joined table

    [Test]
    public async Task A_GMOnly_artifact_is_not_named_to_a_player()
    {
        var secret = SeedArtifact("The Harbourmaster's Ledger", VisibilityScope.GMOnly, ArtifactType.Item);
        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.Artifact, secret.Id);

        var asGm = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);
        var asPlayer = await _sut.GetRollupAsync(_world.Id, _campaign.Id, PlayerFilter, 50);

        Assert.Multiple(() =>
        {
            Assert.That(asGm.Artifacts.Select(a => a.Name), Is.EqualTo(["The Harbourmaster's Ledger"]));
            Assert.That(asPlayer.Artifacts, Is.Empty, "a GMOnly artifact surfaced by name on the campaign page");
            Assert.That(asPlayer.TotalCount, Is.Zero, "the count disclosed a GMOnly artifact's existence");
        });
    }

    [Test]
    public async Task An_artifact_evidenced_only_by_a_GMOnly_source_is_not_named_to_a_player()
    {
        // The artifact is party-visible; the only thing tying it to this campaign is a
        // source the player may not read. Its campaign membership is the secret here.
        var voss = SeedArtifact("Captain Voss");
        SeedReference(SeedSource("GM prep", VisibilityScope.GMOnly),
            SourceReferenceTargetType.Artifact, voss.Id);

        var asGm = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);
        var asPlayer = await _sut.GetRollupAsync(_world.Id, _campaign.Id, PlayerFilter, 50);

        Assert.Multiple(() =>
        {
            Assert.That(asGm.Artifacts, Has.Count.EqualTo(1));
            Assert.That(asPlayer.Artifacts, Is.Empty, "a GMOnly source placed an artifact in the campaign");
        });
    }

    [Test]
    public async Task An_artifact_reachable_only_through_a_GMOnly_fact_is_not_named_to_a_player()
    {
        var voss = SeedArtifact("Captain Voss");
        var secretFact = SeedFact(voss, VisibilityScope.GMOnly);
        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.ArtifactFact, secretFact.Id);

        var asGm = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);
        var asPlayer = await _sut.GetRollupAsync(_world.Id, _campaign.Id, PlayerFilter, 50);

        Assert.Multiple(() =>
        {
            Assert.That(asGm.Artifacts, Has.Count.EqualTo(1));
            Assert.That(asPlayer.Artifacts, Is.Empty, "a GMOnly fact carried its artifact into the campaign");
        });
    }

    [Test]
    public async Task An_artifact_reachable_only_through_a_GMOnly_relationship_is_not_named_to_a_player()
    {
        var voss = SeedArtifact("Captain Voss");
        var caravan = SeedArtifact("Missing Caravan", type: ArtifactType.Event);
        var secretLink = SeedRelationship(voss, caravan, VisibilityScope.GMOnly);
        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.ArtifactRelationship, secretLink.Id);

        var asGm = await _sut.GetRollupAsync(_world.Id, _campaign.Id, VisibilityFilter.All, 50);
        var asPlayer = await _sut.GetRollupAsync(_world.Id, _campaign.Id, PlayerFilter, 50);

        Assert.Multiple(() =>
        {
            Assert.That(asGm.Artifacts, Has.Count.EqualTo(2));
            Assert.That(asPlayer.Artifacts, Is.Empty,
                "a GMOnly relationship carried both its ends into the campaign");
        });
    }

    [Test]
    public async Task Another_users_private_source_does_not_contribute_to_a_players_rollup()
    {
        var voss = SeedArtifact("Captain Voss");
        SeedReference(SeedSource("The GM's private note", VisibilityScope.Private),
            SourceReferenceTargetType.Artifact, voss.Id);

        var asPlayer = await _sut.GetRollupAsync(_world.Id, _campaign.Id, PlayerFilter, 50);

        Assert.That(asPlayer.Artifacts, Is.Empty);
    }

    [Test]
    public async Task A_players_visible_evidence_still_reaches_them_alongside_hidden_evidence()
    {
        // The negative cases above all assert emptiness, which an accidentally-broken query
        // would also satisfy. This is the paired positive: the filter narrows, it does not
        // simply return nothing.
        var voss = SeedArtifact("Captain Voss");
        var ledger = SeedArtifact("The Harbourmaster's Ledger", VisibilityScope.GMOnly, ArtifactType.Item);

        SeedReference(SeedSource("Session 1"), SourceReferenceTargetType.Artifact, voss.Id);
        SeedReference(SeedSource("Session 2"), SourceReferenceTargetType.Artifact, ledger.Id);

        var asPlayer = await _sut.GetRollupAsync(_world.Id, _campaign.Id, PlayerFilter, 50);

        Assert.That(asPlayer.Artifacts.Select(a => a.Name), Is.EqualTo(["Captain Voss"]));
    }

    #endregion
}
