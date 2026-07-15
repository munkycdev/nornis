using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class CharacterServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryCharacterRepository _characterRepository = null!;
    private InMemoryWorldMemberRepository _memberRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private CharacterService _sut = null!;

    private WorldMember _gm = null!;
    private WorldMember _player = null!;
    private WorldMember _otherPlayer = null!;
    private WorldMember _observer = null!;

    [SetUp]
    public async Task SetUp()
    {
        _characterRepository = new InMemoryCharacterRepository();
        _memberRepository = new InMemoryWorldMemberRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _sut = new CharacterService(_characterRepository, _memberRepository, _artifactRepository);

        _gm = await AddMember(WorldRole.GM, "Dave");
        _player = await AddMember(WorldRole.Player, "Tavrin's player");
        _otherPlayer = await AddMember(WorldRole.Player, "Jorin's player");
        _observer = await AddMember(WorldRole.Observer, "Fly");
    }

    private Task<WorldMember> AddMember(WorldRole role, string displayName) =>
        _memberRepository.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = Guid.NewGuid(),
            Role = role,
            DisplayName = displayName,
            JoinedAt = DateTimeOffset.UtcNow
        });

    private Character SeedCharacter(WorldMember owner, string name = "Tavrin")
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            WorldMemberId = owner.Id,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _characterRepository.Seed(character);
        return character;
    }

    // ------------------------------------------------------------------- Create --

    [Test]
    public async Task CreateAsync_MemberCreatesOwnCharacter()
    {
        var command = new CreateCharacterCommand(WorldId, "Tavrin", _player.UserId, WorldRole.Player);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WorldMemberId, Is.EqualTo(_player.Id));
        Assert.That(result.Value.Name, Is.EqualTo("Tavrin"));
    }

    [Test]
    public async Task CreateAsync_MemberCanHaveMultipleCharacters()
    {
        var first = new CreateCharacterCommand(WorldId, "Tavrin", _player.UserId, WorldRole.Player);
        var second = new CreateCharacterCommand(WorldId, "Tavrin's Twin", _player.UserId, WorldRole.Player);

        var firstResult = await _sut.CreateAsync(first, CancellationToken.None);
        var secondResult = await _sut.CreateAsync(second, CancellationToken.None);

        Assert.That(firstResult.IsSuccess, Is.True);
        Assert.That(secondResult.IsSuccess, Is.True);
        Assert.That(_characterRepository.Characters.Count(c => c.WorldMemberId == _player.Id), Is.EqualTo(2));
    }

    [Test]
    public async Task CreateAsync_Observer_Returns403()
    {
        var command = new CreateCharacterCommand(WorldId, "Watcher", _observer.UserId, WorldRole.Observer);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task CreateAsync_GmCreatesForAnotherMember()
    {
        var command = new CreateCharacterCommand(WorldId, "Jorin", _gm.UserId, WorldRole.GM,
            ForWorldMemberId: _otherPlayer.Id);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WorldMemberId, Is.EqualTo(_otherPlayer.Id));
    }

    [Test]
    public async Task CreateAsync_PlayerCreatesForAnotherMember_Returns403()
    {
        var command = new CreateCharacterCommand(WorldId, "Hijack", _player.UserId, WorldRole.Player,
            ForWorldMemberId: _otherPlayer.Id);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task CreateAsync_GmForMemberOutsideWorld_Returns400()
    {
        var command = new CreateCharacterCommand(WorldId, "Stranger", _gm.UserId, WorldRole.GM,
            ForWorldMemberId: Guid.NewGuid());

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task CreateAsync_NonMemberUser_Returns404()
    {
        var command = new CreateCharacterCommand(WorldId, "Ghost", Guid.NewGuid(), WorldRole.Player);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateAsync_BlankName_Returns400(string name)
    {
        var command = new CreateCharacterCommand(WorldId, name, _player.UserId, WorldRole.Player);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    // ------------------------------------------------------------------- Update --

    [Test]
    public async Task UpdateAsync_OwnerEditsOwnCharacter()
    {
        var character = SeedCharacter(_player);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            Name: "Tavrin the Bold");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Name, Is.EqualTo("Tavrin the Bold"));
    }

    [Test]
    public async Task UpdateAsync_OtherPlayer_Returns403()
    {
        var character = SeedCharacter(_player);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player,
            Name: "Vandalized");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task UpdateAsync_GmEditsAnyCharacter()
    {
        var character = SeedCharacter(_player);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _gm.UserId, WorldRole.GM,
            Description: "The party's rogue.");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Description, Is.EqualTo("The party's rogue."));
    }

    [Test]
    public async Task UpdateAsync_WrongWorld_Returns404()
    {
        var character = SeedCharacter(_player);

        var command = new UpdateCharacterCommand(character.Id, Guid.NewGuid(), _gm.UserId, WorldRole.GM, Name: "X");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ------------------------------------------------------- Artifact linking --

    private Artifact SeedArtifact(
        ArtifactType type = ArtifactType.Character,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        Guid? worldId = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = type,
            Name = "Tavrin (record)",
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepository.Seed(artifact);
        return artifact;
    }

    [Test]
    public async Task UpdateAsync_OwnerLinksCharacterArtifact()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact();

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            ArtifactId: artifact.Id);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task UpdateAsync_GmLinksAnyCharacter()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact(visibility: VisibilityScope.GMOnly);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _gm.UserId, WorldRole.GM,
            ArtifactId: artifact.Id);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task UpdateAsync_UnlinkFlag_ClearsTheLink()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact();
        character.ArtifactId = artifact.Id;

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            UnlinkArtifact: true);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ArtifactId, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_NullArtifactId_LeavesExistingLinkUntouched()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact();
        character.ArtifactId = artifact.Id;

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            Name: "Tavrin the Bold");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task UpdateAsync_LinkToNonexistentArtifact_Returns400()
    {
        var character = SeedCharacter(_player);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            ArtifactId: Guid.NewGuid());

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_artifact_link"));
        Assert.That(result.Error.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task UpdateAsync_LinkToWrongTypeArtifact_Returns400()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact(type: ArtifactType.Location);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            ArtifactId: artifact.Id);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_artifact_link"));
    }

    [Test]
    public async Task UpdateAsync_LinkToArtifactInAnotherWorld_Returns400()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact(worldId: Guid.NewGuid());

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            ArtifactId: artifact.Id);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_artifact_link"));
    }

    [Test]
    public async Task UpdateAsync_PlayerLinksGmOnlyArtifact_Returns400()
    {
        var character = SeedCharacter(_player);
        var artifact = SeedArtifact(visibility: VisibilityScope.GMOnly);

        var command = new UpdateCharacterCommand(character.Id, WorldId, _player.UserId, WorldRole.Player,
            ArtifactId: artifact.Id);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_artifact_link"));
    }

    [Test]
    public async Task CreateAsync_WithArtifactId_LinksTheArtifact()
    {
        var artifact = SeedArtifact();

        var command = new CreateCharacterCommand(WorldId, "Ugma", _player.UserId, WorldRole.Player,
            ArtifactId: artifact.Id);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ArtifactId, Is.EqualTo(artifact.Id));
    }

    [Test]
    public async Task CreateAsync_WithWrongTypeArtifact_Returns400()
    {
        var artifact = SeedArtifact(type: ArtifactType.Location);

        var command = new CreateCharacterCommand(WorldId, "Ugma", _player.UserId, WorldRole.Player,
            ArtifactId: artifact.Id);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_artifact_link"));
    }

    // -------------------------------------------------------------------- Claim --

    [Test]
    public async Task ClaimAsync_PlayerClaimsGmOwnedCharacter()
    {
        var character = SeedCharacter(_gm);

        var result = await _sut.ClaimAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WorldMemberId, Is.EqualTo(_player.Id));
    }

    [Test]
    public async Task ClaimAsync_AlreadyOwned_IsNoOp()
    {
        var character = SeedCharacter(_player);
        var before = character.UpdatedAt;

        var result = await _sut.ClaimAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WorldMemberId, Is.EqualTo(_player.Id));
        Assert.That(result.Value.UpdatedAt, Is.EqualTo(before));
    }

    [Test]
    public async Task ClaimAsync_Observer_Returns403()
    {
        var character = SeedCharacter(_gm);

        var result = await _sut.ClaimAsync(character.Id, WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task ClaimAsync_WrongWorld_Returns404()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.ClaimAsync(character.Id, Guid.NewGuid(), _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task ClaimAsync_MissingCharacter_Returns404()
    {
        var result = await _sut.ClaimAsync(Guid.NewGuid(), WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ------------------------------------------------------------------- Delete --

    [Test]
    public async Task DeleteAsync_OwnerDeletes_RemovesAssignments()
    {
        var character = SeedCharacter(_player);
        _characterRepository.SeedAssignments(new CampaignCharacter
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            CharacterId = character.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.DeleteAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_characterRepository.Characters, Is.Empty);
        Assert.That(_characterRepository.Assignments, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_OtherPlayer_Returns403()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.DeleteAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_characterRepository.Characters, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DeleteAsync_Observer_Returns403()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.DeleteAsync(character.Id, WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }
}
