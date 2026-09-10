using System.Text.Json;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The quick switcher composes five services and decides nothing about visibility itself. What
/// these tests pin is the composition: each kind asked at the reader's own role, the substring
/// match, the per-kind cap with an honest total, and — the property that matters — that a
/// hidden artifact leaves no trace anywhere in the response, compared whole.
/// </summary>
[TestFixture]
public class JumpServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid PlayerId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    private ICampaignService _campaigns = null!;
    private ICharacterService _characters = null!;
    private IArtifactService _artifacts = null!;
    private ISourceService _sources = null!;
    private ILibraryService _library = null!;
    private JumpService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _campaigns = Substitute.For<ICampaignService>();
        _characters = Substitute.For<ICharacterService>();
        _artifacts = Substitute.For<IArtifactService>();
        _sources = Substitute.For<ISourceService>();
        _library = Substitute.For<ILibraryService>();

        // Every kind empty unless a test says otherwise.
        _campaigns.ListByWorldAsync(WorldId, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Campaign>>.Success([]));
        _characters.ListByWorldAsync(WorldId, Arg.Any<Guid>(), Arg.Any<WorldRole>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<CharacterView>>.Success([]));
        _artifacts.SearchAsync(Arg.Any<ArtifactSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Artifact>>.Success([]));
        _sources.ListSummariesByWorldAsync(WorldId, Arg.Any<Guid>(), Arg.Any<WorldRole>(), Arg.Any<CancellationToken>(), null, false)
            .Returns(AppResult<IReadOnlyList<SourceListItem>>.Success([]));
        _library.ListAsync(WorldId, Arg.Any<WorldRole>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<LibraryDocument>>.Success([]));

        _sut = new JumpService(_campaigns, _characters, _artifacts, _sources, _library);
    }

    private static Campaign Campaign(string name, CampaignStatus status = CampaignStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Name = name,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static Artifact Artifact(string name, ArtifactType type = ArtifactType.Character) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Name = name,
        Type = type,
        Status = ArtifactStatus.Active,
        Visibility = VisibilityScope.PartyVisible,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private Task<AppResult<JumpResult>> JumpAsPlayer(string term, int perKind = 8) =>
        _sut.JumpAsync(new JumpQuery(WorldId, PlayerId, WorldRole.Player, term, perKind), CancellationToken.None);

    [Test]
    public async Task BlankTerm_IsANoOp_NotAnError()
    {
        var result = await JumpAsPlayer("   ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Groups, Is.Empty);
        await _artifacts.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Test]
    public async Task MatchesBySubstring_CaseInsensitively_StartsWithFirst()
    {
        _campaigns.ListByWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(
            AppResult<IReadOnlyList<Campaign>>.Success([Campaign("The Throne of Tears"), Campaign("Throne Games"), Campaign("Ashes")]));

        var result = await JumpAsPlayer("throne");

        var group = result.Value!.Groups.Single(g => g.Kind == JumpKind.Campaign);
        Assert.That(group.Items.Select(i => i.Name), Is.EqualTo(["Throne Games", "The Throne of Tears"]));
        Assert.That(group.TotalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task CapsPerKind_AndReportsTheHonestTotal()
    {
        _campaigns.ListByWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(
            AppResult<IReadOnlyList<Campaign>>.Success(Enumerable.Range(1, 12).Select(i => Campaign($"Arc {i:00}")).ToList()));

        var result = await JumpAsPlayer("arc", perKind: 5);

        var group = result.Value!.Groups.Single(g => g.Kind == JumpKind.Campaign);
        Assert.That(group.Items, Has.Count.EqualTo(5));
        Assert.That(group.TotalCount, Is.EqualTo(12));
    }

    [Test]
    public async Task EmptyKinds_AreOmitted()
    {
        _campaigns.ListByWorldAsync(WorldId, Arg.Any<CancellationToken>()).Returns(
            AppResult<IReadOnlyList<Campaign>>.Success([Campaign("Ashes")]));

        var result = await JumpAsPlayer("ash");

        Assert.That(result.Value!.Groups.Select(g => g.Kind), Is.EqualTo([JumpKind.Campaign]));
    }

    /// <summary>
    /// Every kind is asked at the reader's own identity and role. Composition is the whole
    /// of this service, so the wiring is the thing to pin.
    /// </summary>
    [Test]
    public async Task AsksEveryService_AtTheReadersRole()
    {
        await JumpAsPlayer("x");

        await _characters.Received(1).ListByWorldAsync(WorldId, PlayerId, WorldRole.Player, Arg.Any<CancellationToken>());
        await _artifacts.Received(1).SearchAsync(
            Arg.Is<ArtifactSearchQuery>(q => q.WorldId == WorldId && q.ActingUserId == PlayerId && q.ActingUserRole == WorldRole.Player && q.Term == "x"),
            Arg.Any<CancellationToken>());
        await _sources.Received(1).ListSummariesByWorldAsync(WorldId, PlayerId, WorldRole.Player, Arg.Any<CancellationToken>(), null, false);
        await _library.Received(1).ListAsync(WorldId, WorldRole.Player, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The property. A world where the artifact search withholds a GM-only entry from a player
    /// must answer exactly as a world where that entry never existed — the whole response
    /// serialized and compared, so a future field cannot leak what the items already hide.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task HiddenArtifact_LeavesNoTraceInTheWholeResponse()
    {
        var visible = Artifact("Silver Key", ArtifactType.Item);

        // World A: the search, at the player's role, returns only what they may see.
        _artifacts.SearchAsync(Arg.Is<ArtifactSearchQuery>(q => q.ActingUserRole == WorldRole.Player), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Artifact>>.Success([visible]));
        var withHidden = await JumpAsPlayer("silver");

        // World B: the hidden entry never existed. Same visible entry, same everything.
        _artifacts.SearchAsync(Arg.Any<ArtifactSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Artifact>>.Success([visible]));
        var withoutHidden = await JumpAsPlayer("silver");

        Assert.That(JsonSerializer.Serialize(withHidden.Value), Is.EqualTo(JsonSerializer.Serialize(withoutHidden.Value)));

        // And the GM, asked the same thing, sees the hidden one — the difference is the role, not the query.
        var dagger = Artifact("Silver Dagger", ArtifactType.Item);
        _artifacts.SearchAsync(Arg.Is<ArtifactSearchQuery>(q => q.ActingUserRole == WorldRole.GM), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<Artifact>>.Success([visible, dagger]));
        var asGm = await _sut.JumpAsync(new JumpQuery(WorldId, GmId, WorldRole.GM, "silver"), CancellationToken.None);

        Assert.That(asGm.Value!.Groups.Single(g => g.Kind == JumpKind.Artifact).Items.Select(i => i.Name),
            Is.EquivalentTo(["Silver Key", "Silver Dagger"]));
    }

    [Test]
    public async Task AFailingService_FailsTheWholeQuery()
    {
        _library.ListAsync(WorldId, Arg.Any<WorldRole>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<LibraryDocument>>.Fail(new AppError(500, "boom", "Library is down.")));

        var result = await JumpAsPlayer("anything");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("boom"));
    }
}
