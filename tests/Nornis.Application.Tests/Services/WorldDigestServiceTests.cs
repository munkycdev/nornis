using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The world digest's two-pass shape. The authorization cases are the point: the party
/// recap renders to every member, so its GENERATION CONTEXT must be the Observer-floor
/// record — the tests read the prompts the fake client captured, because a pass whose
/// context held GM material has already derived from it whatever the output says.
/// </summary>
[TestFixture]
public class WorldDigestServiceTests
{
    private InMemoryWorldDigestRepository _digestRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryAiUsageRecordRepository _usageRepository = null!;
    private FakeWorldDigestAiClient _aiClient = null!;
    private FakeAiBudgetGuard _budgetGuard = null!;
    private WorldDigestService _service = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _digestRepository = new InMemoryWorldDigestRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _sourceRepository = new InMemorySourceRepository();
        _usageRepository = new InMemoryAiUsageRecordRepository();
        _aiClient = new FakeWorldDigestAiClient();
        _budgetGuard = new FakeAiBudgetGuard();

        _service = new WorldDigestService(
            _digestRepository,
            _artifactRepository,
            _factRepository,
            _relationshipRepository,
            _referenceRepository,
            _sourceRepository,
            _aiClient,
            _budgetGuard,
            TestUsageRecorder.Wrap(_usageRepository),
            Options.Create(new LoremasterOptions { AiModel = "gpt-4o", AiEndpoint = "https://test.openai.azure.com/" }));
    }

    private Artifact SeedArtifact(string name, VisibilityScope visibility)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = name,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedByUserId = GmId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _artifactRepository.Seed(artifact);
        return artifact;
    }

    private void SeedFact(
        Guid artifactId, string predicate, string value,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        TruthState truthState = TruthState.Confirmed)
    {
        _factRepository.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
    }

    #region Get

    [Test]
    public async Task Get_NoDigestYet_SaysSoInsteadOfInventingOne()
    {
        var result = await _service.GetAsync(WorldId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.HasData, Is.False);
    }

    [Test]
    public async Task Get_RendersForTheCaller()
    {
        _digestRepository.Seed(new WorldDigest
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            GmContentMarkdown = "GM digest",
            PartyContentMarkdown = "Party recap",
            Model = "gpt-4o",
            GeneratedAt = DateTimeOffset.UtcNow.AddHours(-2),
            GeneratedByUserId = GmId
        });

        var gm = (await _service.GetAsync(WorldId, WorldRole.GM, CancellationToken.None)).Value!;
        Assert.That(gm.Content, Is.EqualTo("GM digest"));
        Assert.That(gm.PartyPreview, Is.EqualTo("Party recap"), "the GM sees what the players will see");

        var player = (await _service.GetAsync(WorldId, WorldRole.Player, CancellationToken.None)).Value!;
        Assert.That(player.Content, Is.EqualTo("Party recap"));
        Assert.That(player.PartyPreview, Is.Null);

        var observer = (await _service.GetAsync(WorldId, WorldRole.Observer, CancellationToken.None)).Value!;
        Assert.That(observer.Content, Is.EqualTo("Party recap"));
    }

    #endregion

    #region Generate — gates

    [Test]
    public async Task Generate_IsGmOnly()
    {
        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
        Assert.That(_aiClient.Requests, Is.Empty);
    }

    [Test]
    public async Task Generate_BudgetExceeded_SpendsNothing()
    {
        SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        _budgetGuard.Exceeded = true;

        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(_aiClient.Requests, Is.Empty);
    }

    [Test]
    public async Task Generate_EmptyWorld_RefusesInsteadOfInventing()
    {
        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("empty_world"));
        Assert.That(_aiClient.Requests, Is.Empty);
    }

    #endregion

    #region Generate — the two passes

    [Test]
    public async Task Generate_PersistsBothRenderings_AndMetersBothPasses()
    {
        SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(_artifactRepository.Artifacts.Single().Id, "occupation", "smuggler");

        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.HasData, Is.True);
        Assert.That(_aiClient.Requests, Has.Count.EqualTo(2), "two renderings are two scoped passes");
        Assert.That(_digestRepository.Digests, Has.Count.EqualTo(1));
        Assert.That(_usageRepository.Records.Count(r => r.OperationType == AiOperationType.WorldDigest),
            Is.EqualTo(2), "each pass meters its own spend");
    }

    [Test]
    [Category("Authorization")]
    public async Task Generate_ThePartyPassContext_IsTheObserverFloor()
    {
        // A party artifact with a party fact and a hidden-truth fact; a GM-only artifact
        // with its own fact; a party-visible relationship ROW whose far end is GM-only.
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(voss.Id, "occupation", "harbormaster");
        SeedFact(voss.Id, "secret plan", "burn the fleet", VisibilityScope.PartyVisible, TruthState.Hidden);

        var silentHand = SeedArtifact("The Silent Hand", VisibilityScope.GMOnly);
        SeedFact(silentHand.Id, "goal", "control the strait", VisibilityScope.GMOnly);

        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = voss.Id,
            ArtifactBId = silentHand.Id,
            Type = "MemberOf",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        });

        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True);

        var gmPrompt = _aiClient.Requests[0].UserMessage;
        Assert.That(gmPrompt, Does.Contain("The Silent Hand"), "the GM pass reads the full record");
        Assert.That(gmPrompt, Does.Contain("burn the fleet"));

        var partyPrompt = _aiClient.Requests[1].UserMessage;
        Assert.That(partyPrompt, Does.Contain("harbormaster"));
        Assert.That(partyPrompt, Does.Not.Contain("The Silent Hand"),
            "a GM-only artifact must not enter the party pass — not even via a party-visible relationship row");
        Assert.That(partyPrompt, Does.Not.Contain("control the strait"));
        Assert.That(partyPrompt, Does.Not.Contain("burn the fleet"),
            "Hidden truth states are GM knowledge regardless of the fact's visibility scope");
    }

    [Test]
    public async Task Generate_NothingPartyVisible_UsesTheFixedRecapWithoutASecondSpend()
    {
        var secret = SeedArtifact("The Silent Hand", VisibilityScope.GMOnly);
        SeedFact(secret.Id, "goal", "control the strait", VisibilityScope.GMOnly);

        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_aiClient.Requests, Has.Count.EqualTo(1),
            "a generation over an empty record could only invent — fixed text instead");
        Assert.That(_digestRepository.Digests.Single().PartyContentMarkdown,
            Is.EqualTo(WorldDigestService.EmptyPartyRecap));
    }

    [Test]
    public async Task Generate_ReplacesTheDigest_NotAppendsToIt()
    {
        SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(_artifactRepository.Artifacts.Single().Id, "occupation", "smuggler");

        _aiClient.DigestToReturn = "First digest.";
        await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);
        _aiClient.DigestToReturn = "Second digest.";
        await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        var digest = _digestRepository.Digests.Single();
        Assert.That(digest.GmContentMarkdown, Is.EqualTo("Second digest."));
    }

    [Test]
    public async Task Generate_AiFailure_MetersTheAttemptAndReportsServiceUnavailable()
    {
        SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(_artifactRepository.Artifacts.Single().Id, "occupation", "smuggler");
        _aiClient.ExceptionToThrow = new Nornis.Application.Ai.AiTimeoutException("timed out");

        var result = await _service.GenerateAsync(WorldId, GmId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("service_unavailable"));
        Assert.That(_usageRepository.Records.Single().Succeeded, Is.False, "the failed attempt still meters");
        Assert.That(_digestRepository.Digests, Is.Empty, "no half-generated digest is persisted");
    }

    #endregion
}
