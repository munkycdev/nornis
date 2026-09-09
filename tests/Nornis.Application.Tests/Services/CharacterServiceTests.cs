using Nornis.Application.Errors;
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
    private InMemoryCampaignRepository _campaignRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemoryCharacterSheetSnapshotRepository _snapshotRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
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
        _campaignRepository = new InMemoryCampaignRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _snapshotRepository = new InMemoryCharacterSheetSnapshotRepository();
        _sourceRepository = new InMemorySourceRepository();

        // A real ArtifactService over the same repositories, not a stub. The dossier's whole
        // visibility contract is that it inherits this service's filtering, and a stub that
        // returned whatever the test wanted would prove exactly nothing about that.
        var artifactService = new ArtifactService(
            _artifactRepository, _factRepository, _relationshipRepository,
            new InMemorySourceReferenceRepository(), _sourceRepository,
            _characterRepository, _memberRepository, _campaignRepository);

        _sut = new CharacterService(
            _characterRepository, _memberRepository, _artifactRepository,
            _campaignRepository, _snapshotRepository, _sourceRepository, artifactService);

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

    [Category("Authorization")]
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

    [Category("Authorization")]
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

    [Category("Authorization")]
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
        Guid? worldId = null,
        string? name = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = type,
            Name = name ?? "Tavrin (record)",
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

    [Category("Authorization")]
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

    [Category("Authorization")]
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

    [Category("Authorization")]
    public async Task DeleteAsync_OtherPlayer_Returns403()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.DeleteAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_characterRepository.Characters, Has.Count.EqualTo(1));
    }

    [Test]

    [Category("Authorization")]
    public async Task DeleteAsync_Observer_Returns403()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.DeleteAsync(character.Id, WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    // ------------------------------------------------------------------ Dossier --

    private void SeedFact(Artifact artifact, string predicate, string value, VisibilityScope visibility)
    {
        _factRepository.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = predicate,
            Value = value,
            TruthState = TruthState.Confirmed,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private void SeedRelationship(Artifact a, Artifact b, VisibilityScope visibility)
    {
        _relationshipRepository.Seed(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = a.Id,
            ArtifactBId = b.Id,
            Type = "Carries",
            TruthState = TruthState.Confirmed,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private Character SeedLinkedCharacter(WorldMember owner, Artifact artifact, string name = "Tavrin")
    {
        var character = SeedCharacter(owner, name);
        character.ArtifactId = artifact.Id;
        return character;
    }

    /// <summary>
    /// Property 3. A link the reader may not follow must be indistinguishable from no link at
    /// all, or the page announces that a GM-only artifact exists bearing this character's name.
    ///
    /// Compared as the <em>whole</em> dossier, serialized, with only the identity fields that
    /// legitimately differ (id, name, timestamps) made equal first. An earlier version of this
    /// test compared <c>Record</c> alone and passed for a month while the character envelope
    /// carried the hidden artifact's id — the pages were indistinguishable and the JSON was
    /// not. Whatever field is added to the dossier next is covered by this test without anyone
    /// remembering to add it.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_HiddenLink_IsIndistinguishableFromNoLink()
    {
        var hidden = SeedArtifact(visibility: VisibilityScope.GMOnly);
        SeedFact(hidden, "true name", "Tavrin Ashgrave", VisibilityScope.GMOnly);
        var linked = SeedLinkedCharacter(_player, hidden, "Tavrin");
        var unlinked = SeedCharacter(_player, "Jorin");

        var linkedResult = await _sut.GetDossierAsync(
            linked.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);
        var unlinkedResult = await _sut.GetDossierAsync(
            unlinked.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(linkedResult.IsSuccess, Is.True);
        Assert.That(unlinkedResult.IsSuccess, Is.True);

        var target = unlinkedResult.Value!;
        var normalized = linkedResult.Value! with
        {
            Character = linkedResult.Value!.Character with
            {
                Id = target.Character.Id,
                Name = target.Character.Name,
                CreatedAt = target.Character.CreatedAt,
                UpdatedAt = target.Character.UpdatedAt
            }
        };

        Assert.That(
            System.Text.Json.JsonSerializer.Serialize(normalized),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(target)));
    }

    /// <summary>
    /// The same property on the plainer read paths. The list and the single read predate the
    /// dossier and served the entity's <c>ArtifactId</c> to every member; the dossier inherited
    /// the leak from them, so the fix has to hold here or it holds nowhere.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task ListByWorldAsync_HiddenLink_ReadsAsUnlinked_ExceptToTheGm()
    {
        var hidden = SeedArtifact(visibility: VisibilityScope.GMOnly);
        var shown = SeedArtifact(visibility: VisibilityScope.PartyVisible);
        var secret = SeedLinkedCharacter(_player, hidden, "Tavrin");
        var open = SeedLinkedCharacter(_player, shown, "Jorin");

        var asOwner = await _sut.ListByWorldAsync(WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        var asOther = await _sut.ListByWorldAsync(WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);
        var asObserver = await _sut.ListByWorldAsync(WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);
        var asGm = await _sut.ListByWorldAsync(WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Guid? LinkOf(AppResult<IReadOnlyList<CharacterView>> result, Guid characterId) =>
            result.Value!.Single(c => c.Id == characterId).ArtifactId;

        Assert.Multiple(() =>
        {
            // Property 2 again: owning the character does not widen what its owner may see.
            Assert.That(LinkOf(asOwner, secret.Id), Is.Null);
            Assert.That(LinkOf(asOther, secret.Id), Is.Null);
            Assert.That(LinkOf(asObserver, secret.Id), Is.Null);
            Assert.That(LinkOf(asGm, secret.Id), Is.EqualTo(hidden.Id));

            Assert.That(LinkOf(asOwner, open.Id), Is.EqualTo(shown.Id));
            Assert.That(LinkOf(asObserver, open.Id), Is.EqualTo(shown.Id));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task GetByIdAsync_HiddenLink_ReadsAsUnlinked()
    {
        var hidden = SeedArtifact(visibility: VisibilityScope.GMOnly);
        var character = SeedLinkedCharacter(_player, hidden);

        var asPlayer = await _sut.GetByIdAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        var asGm = await _sut.GetByIdAsync(character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(asPlayer.Value!.ArtifactId, Is.Null);
            Assert.That(asGm.Value!.ArtifactId, Is.EqualTo(hidden.Id));
        });
    }

    /// <summary>
    /// A link to an artifact that no longer resolves fails closed to "unlinked" for everyone,
    /// GM included — the same answer the dossier's record gives for it.
    /// </summary>
    [Test]
    public async Task GetByIdAsync_DanglingLink_ReadsAsUnlinked()
    {
        var character = SeedCharacter(_player);
        character.ArtifactId = Guid.NewGuid();

        var asGm = await _sut.GetByIdAsync(character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.That(asGm.Value!.ArtifactId, Is.Null);
    }

    /// <summary>
    /// The sheet's timestamp obeys the sheet's read gate. A reader who may not open the sheet
    /// must not learn from its timestamp that one exists to be shared — the disclosure
    /// <c>SheetSharedWithParty</c> already declines to make.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task ListByWorldAsync_SheetTimestamp_FollowsTheSheetReadGate()
    {
        var character = await SeedSheet(_player, "AC 16");
        character.SheetUpdatedAt = DateTimeOffset.UtcNow;
        await _characterRepository.UpdateAsync(character, CancellationToken.None);

        DateTimeOffset? StampFor(AppResult<IReadOnlyList<CharacterView>> result) =>
            result.Value!.Single(c => c.Id == character.Id).SheetUpdatedAt;

        var unsharedOwner = StampFor(await _sut.ListByWorldAsync(WorldId, _player.UserId, WorldRole.Player, CancellationToken.None));
        var unsharedGm = StampFor(await _sut.ListByWorldAsync(WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None));
        var unsharedOther = StampFor(await _sut.ListByWorldAsync(WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None));
        var unsharedObserver = StampFor(await _sut.ListByWorldAsync(WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None));

        character.SheetSharedWithParty = true;
        await _characterRepository.UpdateAsync(character, CancellationToken.None);

        var sharedOther = StampFor(await _sut.ListByWorldAsync(WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(unsharedOwner, Is.Not.Null);
            Assert.That(unsharedGm, Is.Not.Null);
            Assert.That(unsharedOther, Is.Null);
            Assert.That(unsharedObserver, Is.Null);
            Assert.That(sharedOther, Is.Not.Null);
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_HiddenLink_IsVisibleToGm()
    {
        var hidden = SeedArtifact(visibility: VisibilityScope.GMOnly);
        var character = SeedLinkedCharacter(_player, hidden);

        var result = await _sut.GetDossierAsync(
            character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Record, Is.Not.Null);
        Assert.That(result.Value!.Record!.ArtifactId, Is.EqualTo(hidden.Id));
    }

    /// <summary>
    /// Property 2. Owning the character must not widen what its owner may see of the artifact
    /// behind it — the reader's own role governs, and nothing else.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_OwnershipDoesNotWidenVisibility()
    {
        var artifact = SeedArtifact();
        SeedFact(artifact, "carries", "the Silver Key", VisibilityScope.PartyVisible);
        SeedFact(artifact, "true name", "Tavrin Ashgrave", VisibilityScope.GMOnly);
        var character = SeedLinkedCharacter(_player, artifact);

        var owner = await _sut.GetDossierAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        var gm = await _sut.GetDossierAsync(
            character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Value!.Record!.Facts.Select(f => f.Predicate), Is.EquivalentTo(["carries"]));
            Assert.That(gm.Value!.Record!.Facts.Select(f => f.Predicate),
                Is.EquivalentTo(["carries", "true name"]));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_GroupsOnlyVisibleConnections()
    {
        var artifact = SeedArtifact();
        var key = SeedArtifact(ArtifactType.Item, name: "Silver Key");
        var dagger = SeedArtifact(ArtifactType.Item, VisibilityScope.GMOnly, name: "Cursed Dagger");
        SeedRelationship(artifact, key, VisibilityScope.PartyVisible);
        SeedRelationship(artifact, dagger, VisibilityScope.GMOnly);
        var character = SeedLinkedCharacter(_player, artifact);

        var result = await _sut.GetDossierAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        var items = result.Value!.Record!.Groups.Single(g => g.Type == ArtifactType.Item);
        Assert.Multiple(() =>
        {
            Assert.That(items.Artifacts.Select(a => a.Name), Is.EquivalentTo(["Silver Key"]));
            Assert.That(items.TotalCount, Is.EqualTo(1), "TotalCount must count only what the reader may see.");
        });
    }

    /// <summary>
    /// Requirement 6.4. The observation is computed from the filtered record and the gated
    /// sheet, so a GM-only item the owner cannot see is not "missing from their sheet" — it is
    /// not there at all. And a reader who cannot open the sheet gets no observation, since an
    /// observation about a sheet you cannot read would be a second way of reading it.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_UnreconciledItems_ObeyBothGates()
    {
        var artifact = SeedArtifact();
        var key = SeedArtifact(ArtifactType.Item, name: "Silver Key");
        var dagger = SeedArtifact(ArtifactType.Item, VisibilityScope.GMOnly, name: "Cursed Dagger");
        SeedRelationship(artifact, key, VisibilityScope.PartyVisible);
        SeedRelationship(artifact, dagger, VisibilityScope.GMOnly);
        var character = SeedLinkedCharacter(_player, artifact);
        character.Sheet = "Equipment: rope, rations. Nothing shiny yet.";
        await _characterRepository.UpdateAsync(character, CancellationToken.None);

        var owner = await _sut.GetDossierAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        var gm = await _sut.GetDossierAsync(character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);
        var other = await _sut.GetDossierAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Value!.UnreconciledItems.Select(u => u.Name), Is.EquivalentTo(["Silver Key"]));
            Assert.That(gm.Value!.UnreconciledItems.Select(u => u.Name), Is.EquivalentTo(["Silver Key", "Cursed Dagger"]));
            Assert.That(other.Value!.UnreconciledItems, Is.Empty, "no readable sheet, no observation");
        });
    }

    [Test]
    [TestCase(WorldRole.GM)]
    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    [Category("Authorization")]
    public async Task GetDossierAsync_EveryRoleMayRead(WorldRole role)
    {
        var character = SeedCharacter(_player);
        var reader = role switch
        {
            WorldRole.GM => _gm,
            WorldRole.Observer => _observer,
            _ => _otherPlayer
        };

        var result = await _sut.GetDossierAsync(
            character.Id, WorldId, reader.UserId, role, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task GetDossierAsync_OtherWorld_Returns404()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.GetDossierAsync(
            character.Id, Guid.NewGuid(), _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // -------------------------------------------------------------- Written sheet --

    private async Task<Character> SeedSheet(WorldMember owner, string text, bool shared = false)
    {
        var character = SeedCharacter(owner);
        character.Sheet = text;
        character.SheetSharedWithParty = shared;
        return await _characterRepository.UpdateAsync(character, CancellationToken.None);
    }

    /// <summary>
    /// The read gate. An unshared sheet is its owner's and the GM's; nobody else gets the text.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_UnsharedSheet_IsHiddenFromOtherMembers()
    {
        var character = await SeedSheet(_player, "AC 16, Silver Key, owes Voss a favour");

        var owner = await _sut.GetDossierAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        var gm = await _sut.GetDossierAsync(character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);
        var other = await _sut.GetDossierAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);
        var observer = await _sut.GetDossierAsync(character.Id, WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Value!.Sheet, Is.EqualTo("AC 16, Silver Key, owes Voss a favour"));
            Assert.That(gm.Value!.Sheet, Is.EqualTo("AC 16, Silver Key, owes Voss a favour"));
            Assert.That(other.Value!.Sheet, Is.Null);
            Assert.That(observer.Value!.Sheet, Is.Null);
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_SharedSheet_IsReadableByTheWholeWorld()
    {
        var character = await SeedSheet(_player, "Silver Key, ashen cloak", shared: true);

        var other = await _sut.GetDossierAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);
        var observer = await _sut.GetDossierAsync(character.Id, WorldId, _observer.UserId, WorldRole.Observer, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(other.Value!.Sheet, Is.EqualTo("Silver Key, ashen cloak"));
            Assert.That(observer.Value!.Sheet, Is.EqualTo("Silver Key, ashen cloak"));
        });
    }

    /// <summary>
    /// A reader who cannot read the sheet has no business learning whether one exists to share,
    /// so the sharing flag is reported only to readers who may edit.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_SharingFlagIsOnlyMeaningfulToEditors()
    {
        var character = await SeedSheet(_player, "private notes", shared: false);

        var other = await _sut.GetDossierAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(other.Value!.SheetSharedWithParty, Is.False);
            Assert.That(other.Value!.CanEditSheet, Is.False);
            Assert.That(other.Value!.CanShareSheet, Is.False);
        });
    }

    [Test]
    public async Task GetDossierAsync_OnlyTheOwnerMayShare()
    {
        var character = SeedCharacter(_player);

        var owner = await _sut.GetDossierAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);
        var gm = await _sut.GetDossierAsync(character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(owner.Value!.CanShareSheet, Is.True);
            Assert.That(gm.Value!.CanEditSheet, Is.True, "a GM may edit any character's sheet");
            Assert.That(gm.Value!.CanShareSheet, Is.False, "but a GM does not decide who else reads it");
        });
    }

    [Test]
    public async Task UpdateSheetAsync_OwnerWritesSheet()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.UpdateSheetAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, "Level 4, Silver Key", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Sheet, Is.EqualTo("Level 4, Silver Key"));
        Assert.That(result.Value!.SheetUpdatedAt, Is.Not.Null);
    }

    [Test]
    public async Task UpdateSheetAsync_GmMayWriteAnothersSheet()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.UpdateSheetAsync(
            character.Id, WorldId, _gm.UserId, WorldRole.GM, "GM correction", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    [Category("Authorization")]
    public async Task UpdateSheetAsync_OtherPlayer_Returns403()
    {
        var character = SeedCharacter(_player);

        var result = await _sut.UpdateSheetAsync(
            character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, "not mine", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task UpdateSheetAsync_EmptyInput_NormalizesToNull(string? input)
    {
        var character = await SeedSheet(_player, "something");

        var result = await _sut.UpdateSheetAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, input, CancellationToken.None);

        Assert.That(result.Value!.Sheet, Is.Null,
            "never-written and deliberately-cleared must have one representation, not two");
    }

    [Test]
    public async Task UpdateSheetAsync_OverLength_IsRefusedNotTruncated()
    {
        var character = SeedCharacter(_player);
        var tooLong = new string('x', Character.MaxSheetChars + 1);

        var result = await _sut.UpdateSheetAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, tooLong, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(_characterRepository.Characters.Single().Sheet, Is.Null,
                "a refused write must leave the stored sheet untouched");
        });
    }

    [Test]
    public async Task UpdateSheetAsync_AtTheLimit_IsAccepted()
    {
        var character = SeedCharacter(_player);
        var atLimit = new string('x', Character.MaxSheetChars);

        var result = await _sut.UpdateSheetAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, atLimit, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task SetSheetSharingAsync_OwnerShares()
    {
        var character = await SeedSheet(_player, "shared soon");

        var result = await _sut.SetSheetSharingAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, true, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.SheetSharedWithParty, Is.True);
    }

    [Test]
    [Category("Authorization")]
    public async Task SetSheetSharingAsync_Gm_Returns403()
    {
        var character = await SeedSheet(_player, "the GM may read this but not publish it");

        var result = await _sut.SetSheetSharingAsync(
            character.Id, WorldId, _gm.UserId, WorldRole.GM, true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(_characterRepository.Characters.Single().SheetSharedWithParty, Is.False);
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task SetSheetSharingAsync_Observer_Returns403()
    {
        var character = SeedCharacter(_observer);

        var result = await _sut.SetSheetSharingAsync(
            character.Id, WorldId, _observer.UserId, WorldRole.Observer, true, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    // ------------------------------------------------------------ Sheet snapshots --

    private Source SeedSource(
        WorldMember creator,
        string title = "Tavrin's sheet, session 6",
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        Guid? worldId = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = SourceType.HandwrittenNotes,
            Title = title,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = creator.UserId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private static readonly DateTimeOffset AsOf = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AttachSnapshotAsync_OwnerAttachesReadableSource()
    {
        var character = SeedCharacter(_player);
        var source = SeedSource(_player);

        var result = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, source.Id, AsOf, "after the level-up", _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_snapshotRepository.Snapshots, Has.Count.EqualTo(1));
        Assert.That(result.Value!.AsOf, Is.EqualTo(AsOf));
    }

    [Test]
    [Category("Authorization")]
    public async Task AttachSnapshotAsync_OtherPlayer_Returns403()
    {
        var character = SeedCharacter(_player);
        var source = SeedSource(_otherPlayer);

        var result = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, source.Id, AsOf, null, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    /// <summary>
    /// A missing source, another world's source, and a source above the caller's visibility
    /// must be one answer. Distinguishing them turns the endpoint into an id oracle.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task AttachSnapshotAsync_UnusableSources_ShareOneError()
    {
        var character = SeedCharacter(_player);
        var otherWorld = SeedSource(_player, worldId: Guid.NewGuid());
        var gmOnly = SeedSource(_gm, "GM prep", VisibilityScope.GMOnly);

        var missing = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, Guid.NewGuid(), AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);
        var foreign = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, otherWorld.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);
        var hidden = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, gmOnly.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(missing.Error!.Code, Is.EqualTo("invalid_source"));
            Assert.That(foreign.Error!.Code, Is.EqualTo(missing.Error!.Code));
            Assert.That(hidden.Error!.Code, Is.EqualTo(missing.Error!.Code));
            Assert.That(hidden.Error!.Message, Is.EqualTo(missing.Error!.Message));
        });
    }

    [Test]
    public async Task AttachSnapshotAsync_SameSourceTwice_IsRefused()
    {
        var character = SeedCharacter(_player);
        var source = SeedSource(_player);

        await _sut.AttachSnapshotAsync(character.Id, WorldId, source.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);
        var second = await _sut.AttachSnapshotAsync(character.Id, WorldId, source.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(second.IsSuccess, Is.False);
        Assert.That(_snapshotRepository.Snapshots, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AttachSnapshotAsync_BeyondTheCap_IsRefusedNotEvicted()
    {
        var character = SeedCharacter(_player);

        for (var i = 0; i < CharacterSheetSnapshot.MaxSnapshots; i++)
        {
            _snapshotRepository.Seed(new CharacterSheetSnapshot
            {
                Id = Guid.NewGuid(),
                CharacterId = character.Id,
                SourceId = Guid.NewGuid(),
                AsOf = AsOf,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = _player.UserId
            });
        }

        var source = SeedSource(_player);
        var result = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, source.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
            Assert.That(_snapshotRepository.Snapshots, Has.Count.EqualTo(CharacterSheetSnapshot.MaxSnapshots),
                "the oldest must not be evicted to make room — that deletes the history this feature is for");
        });
    }

    /// <summary>
    /// Property 5: a snapshot is readable exactly when its source is, and this feature adds no
    /// second rule for the same bytes.
    /// </summary>
    [Test]
    [Category("Authorization")]
    public async Task GetDossierAsync_SnapshotsFollowTheirSourcesVisibility()
    {
        var character = SeedCharacter(_player);
        var open = SeedSource(_player, "Sheet, session 6");
        var hidden = SeedSource(_gm, "GM's copy", VisibilityScope.GMOnly);

        await _sut.AttachSnapshotAsync(character.Id, WorldId, open.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);
        await _sut.AttachSnapshotAsync(character.Id, WorldId, hidden.Id, AsOf, null, _gm.UserId, WorldRole.GM, CancellationToken.None);

        var asPlayer = await _sut.GetDossierAsync(character.Id, WorldId, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);
        var asGm = await _sut.GetDossierAsync(character.Id, WorldId, _gm.UserId, WorldRole.GM, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(asPlayer.Value!.Snapshots.Select(s => s.SourceTitle),
                Is.EquivalentTo(["Sheet, session 6"]));
            Assert.That(asGm.Value!.Snapshots, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task DetachSnapshotAsync_RemovesTheAttachmentAndKeepsTheSource()
    {
        var character = SeedCharacter(_player);
        var source = SeedSource(_player);
        var attached = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, source.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);

        var result = await _sut.DetachSnapshotAsync(
            character.Id, WorldId, attached.Value!.Id, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_snapshotRepository.Snapshots, Is.Empty);
            Assert.That(_sourceRepository.Sources.Any(s => s.Id == source.Id), Is.True,
                "detaching must never delete the source — the ledger stays the record");
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task DetachSnapshotAsync_OtherPlayer_Returns403()
    {
        var character = SeedCharacter(_player);
        var source = SeedSource(_player);
        var attached = await _sut.AttachSnapshotAsync(
            character.Id, WorldId, source.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);

        var result = await _sut.DetachSnapshotAsync(
            character.Id, WorldId, attached.Value!.Id, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_snapshotRepository.Snapshots, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DetachSnapshotAsync_AnotherCharactersSnapshot_LeavesItAlone()
    {
        var mine = SeedCharacter(_player, "Tavrin");
        var theirs = SeedCharacter(_otherPlayer, "Jorin");
        var source = SeedSource(_otherPlayer);
        var attached = await _sut.AttachSnapshotAsync(
            theirs.Id, WorldId, source.Id, AsOf, null, _otherPlayer.UserId, WorldRole.Player, CancellationToken.None);

        var result = await _sut.DetachSnapshotAsync(
            mine.Id, WorldId, attached.Value!.Id, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, "not-there and not-yours are both idempotent success");
        Assert.That(_snapshotRepository.Snapshots, Has.Count.EqualTo(1), "and neither may delete another's row");
    }

    [Test]
    public async Task GetDossierAsync_SnapshotsAreNewestFirst()
    {
        var character = SeedCharacter(_player);
        var older = SeedSource(_player, "session 4");
        var newer = SeedSource(_player, "session 9");

        await _sut.AttachSnapshotAsync(character.Id, WorldId, older.Id, AsOf, null, _player.UserId, WorldRole.Player, CancellationToken.None);
        await _sut.AttachSnapshotAsync(character.Id, WorldId, newer.Id, AsOf.AddMonths(2), null, _player.UserId, WorldRole.Player, CancellationToken.None);

        var result = await _sut.GetDossierAsync(character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.Value!.Snapshots.Select(s => s.SourceTitle),
            Is.EqualTo(["session 9", "session 4"]).AsCollection);
    }

    [Test]
    public async Task GetDossierAsync_NamesOwnerAndCampaigns()
    {
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = "Vespergale Reach",
            Status = CampaignStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _campaignRepository.Seed(campaign);

        var character = SeedCharacter(_player);
        await _characterRepository.ReplaceCampaignAssignmentsAsync(
            campaign.Id, [character.Id], CancellationToken.None);

        var result = await _sut.GetDossierAsync(
            character.Id, WorldId, _player.UserId, WorldRole.Player, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.OwnerDisplayName, Is.EqualTo("Tavrin's player"));
            Assert.That(result.Value!.CampaignNames, Is.EquivalentTo(["Vespergale Reach"]));
        });
    }
}
