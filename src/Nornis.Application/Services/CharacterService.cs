using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly ICampaignRepository _campaignRepository;
    private readonly ICharacterSheetSnapshotRepository _snapshotRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IArtifactService _artifactService;

    public CharacterService(
        ICharacterRepository characterRepository,
        IWorldMemberRepository worldMemberRepository,
        IPlayerRepository playerRepository,
        IArtifactRepository artifactRepository,
        ICampaignRepository campaignRepository,
        ICharacterSheetSnapshotRepository snapshotRepository,
        ISourceRepository sourceRepository,
        IArtifactService artifactService)
    {
        _characterRepository = characterRepository;
        _worldMemberRepository = worldMemberRepository;
        _playerRepository = playerRepository;
        _artifactRepository = artifactRepository;
        _campaignRepository = campaignRepository;
        _snapshotRepository = snapshotRepository;
        _sourceRepository = sourceRepository;
        _artifactService = artifactService;
    }

    public async Task<AppResult<Character>> CreateAsync(CreateCharacterCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole == WorldRole.Observer)
        {
            return AppResult<Character>.Fail(new AppError(403, "insufficient_role", "Observers cannot create characters."));
        }

        var nameError = ValidateName(command.Name);
        if (nameError is not null)
        {
            return AppResult<Character>.Fail(nameError);
        }

        var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);
        if (actingMember is null)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "World membership not found."));
        }

        var ownPlayer = await _playerRepository.GetOrCreateByMemberAsync(actingMember, ct);
        var playerId = ownPlayer.Id;

        if (command.ForPlayerId is not null && command.ForPlayerId != ownPlayer.Id)
        {
            if (command.ActingUserRole != WorldRole.GM)
            {
                return AppResult<Character>.Fail(new AppError(403, "forbidden", "Only GMs can create characters for other players."));
            }

            var target = await _playerRepository.GetByIdAsync(command.ForPlayerId.Value, ct);
            if (target is null || target.WorldId != command.WorldId)
            {
                return AppResult<Character>.Fail(new AppError(400, "invalid_player", "The target player does not belong to this world."));
            }

            playerId = target.Id;
        }

        if (command.ArtifactId is { } artifactId)
        {
            var linkError = await ValidateArtifactLinkAsync(artifactId, command.WorldId, command.ActingUserRole, ct);
            if (linkError is not null)
            {
                return AppResult<Character>.Fail(linkError);
            }
        }

        var now = DateTimeOffset.UtcNow;

        var character = new Character
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            PlayerId = playerId,
            Name = command.Name.Trim(),
            Description = command.Description,
            ArtifactId = command.ArtifactId,
            CreatedAt = now,
            UpdatedAt = now
        };

        character = await _characterRepository.CreateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult<CharacterView>> GetByIdAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<CharacterView>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var views = await ProjectForReaderAsync([character], worldId, actingUserId, role, ct);
        return AppResult<CharacterView>.Success(views[0]);
    }

    public async Task<AppResult<CharacterDossier>> GetDossierAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<CharacterDossier>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var members = await _worldMemberRepository.ListByWorldAsync(worldId, ct);
        var player = await _playerRepository.GetByIdAsync(character.PlayerId, ct);

        var campaigns = await _campaignRepository.ListByWorldAsync(worldId, ct);
        var campaignNames = character.CampaignCharacters
            .Select(cc => campaigns.FirstOrDefault(c => c.Id == cc.CampaignId))
            .Where(c => c is not null)
            .Select(c => c!.Name)
            .OrderBy(name => name)
            .ToList();

        var record = await ResolveRecordAsync(character, worldId, actingUserId, role, ct);

        var actingMember = members.FirstOrDefault(m => m.UserId == actingUserId);
        var isSteward = IsSteward(player, actingMember, role);
        var canEditSheet = isSteward || role == WorldRole.GM;
        var canReadSheet = CanReadSheet(character, player, actingMember, role);

        // The envelope goes through the same projection as the list, so the two paths cannot
        // drift in what they disclose. Visible-artifact resolution is the one query the
        // projection needs; here it is the record already resolved above.
        var view = ToView(
            character,
            player,
            PlayerNameFor(player, members),
            actingMember,
            role,
            artifactVisible: record is not null);

        var readableSheet = canReadSheet ? character.Sheet : null;

        var dossier = new CharacterDossier(
            Character: view,
            PlayerName: PlayerNameFor(player, members),
            CampaignNames: campaignNames,
            Record: record,
            Sheet: readableSheet,
            SheetSharedWithParty: canEditSheet && character.SheetSharedWithParty,
            CanEditSheet: canEditSheet,
            CanShareSheet: isSteward,
            Snapshots: await ResolveSnapshotsAsync(characterId, actingUserId, role, ct),
            // Fed the filtered record and the gated sheet, so it cannot say anything the two
            // gates above did not already allow.
            UnreconciledItems: UnreconciledItems.Find(record, readableSheet));

        return AppResult<CharacterDossier>.Success(dossier);
    }

    /// <summary>
    /// The linked artifact as this reader may see it, or null.
    ///
    /// <c>GetDetailAsync</c> answers 404 both for an artifact that is gone and for one above
    /// the reader's visibility, and this collapses that into the same null an unlinked
    /// character produces. The collapse is the feature, not a shortcut: any observable
    /// difference between "no link" and "a link you may not follow" discloses that a GM-only
    /// artifact exists bearing this character's name.
    ///
    /// The reader's own role is passed through unchanged. Owning a character must not widen
    /// what its owner may see of the artifact behind it.
    /// </summary>
    private async Task<CharacterRecord?> ResolveRecordAsync(
        Character character,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        if (character.ArtifactId is not { } artifactId)
        {
            return null;
        }

        var detail = await _artifactService.GetDetailAsync(artifactId, worldId, actingUserId, role, ct);

        return detail.IsSuccess ? CharacterRecordProjector.Project(detail.Value!) : null;
    }

    /// <summary>
    /// The character's sheet snapshots, limited to those whose source this reader may read.
    ///
    /// Visibility comes from <c>ListAttributionByIdsAsync</c> — the same projected, SQL-applied
    /// <c>SourceVisibilityRule</c> that decides which provenance rows an artifact page may show.
    /// A snapshot is a source wearing a date, so it is readable exactly when its source is, and
    /// this feature contributes no second rule for the same bytes. Ids that no longer resolve
    /// are simply absent, which fails closed.
    /// </summary>
    private async Task<IReadOnlyList<CharacterSnapshotView>> ResolveSnapshotsAsync(
        Guid characterId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        var snapshots = await _snapshotRepository.ListByCharacterAsync(characterId, ct);

        if (snapshots.Count == 0)
        {
            return [];
        }

        var attributions = await _sourceRepository.ListAttributionByIdsAsync(
            snapshots.Select(s => s.SourceId).Distinct().ToList(), actingUserId, role, ct);

        var titles = attributions.ToDictionary(a => a.Id);

        return snapshots
            .Where(s => titles.ContainsKey(s.SourceId))
            .Select(s => new CharacterSnapshotView(
                Id: s.Id,
                SourceId: s.SourceId,
                SourceTitle: titles[s.SourceId].Title,
                AsOf: s.AsOf,
                Note: s.Note,
                SourceSlug: titles[s.SourceId].Slug))
            .ToList();
    }

    /// <summary>
    /// Attaches an existing source to the character as a dated snapshot of its sheet.
    ///
    /// Every rejection below answers with the same 400, so probing ids cannot distinguish
    /// "no such source", "another world's source" and "a source you may not read".
    /// </summary>
    public async Task<AppResult<CharacterSheetSnapshot>> AttachSnapshotAsync(
        Guid characterId,
        Guid worldId,
        Guid sourceId,
        DateTimeOffset asOf,
        string? note,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<CharacterSheetSnapshot>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var ownershipError = await CheckStewardshipAsync(character, actingUserId, role, ct);
        if (ownershipError is not null)
        {
            return AppResult<CharacterSheetSnapshot>.Fail(ownershipError);
        }

        var invalidSource = new AppError(400, "invalid_source",
            "The snapshot must be a source in this world that you can read.");

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId)
        {
            return AppResult<CharacterSheetSnapshot>.Fail(invalidSource);
        }

        var readable = await _sourceRepository.ListAttributionByIdsAsync([sourceId], actingUserId, role, ct);
        if (readable.Count == 0)
        {
            return AppResult<CharacterSheetSnapshot>.Fail(invalidSource);
        }

        var existing = await _snapshotRepository.ListByCharacterAsync(characterId, ct);

        if (existing.Any(s => s.SourceId == sourceId))
        {
            return AppResult<CharacterSheetSnapshot>.Fail(new AppError(400, "already_attached",
                "That source is already attached to this character."));
        }

        if (existing.Count >= CharacterSheetSnapshot.MaxSnapshots)
        {
            return AppResult<CharacterSheetSnapshot>.Fail(new AppError(400, "validation_error",
                $"A character may keep {CharacterSheetSnapshot.MaxSnapshots} sheet snapshots. "
                + "Detach one before adding another."));
        }

        var snapshot = await _snapshotRepository.CreateAsync(new CharacterSheetSnapshot
        {
            Id = Guid.NewGuid(),
            CharacterId = characterId,
            SourceId = sourceId,
            AsOf = asOf,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = actingUserId
        }, ct);

        return AppResult<CharacterSheetSnapshot>.Success(snapshot);
    }

    /// <summary>
    /// Detaches a snapshot. The source itself is untouched — the ledger remains the record.
    /// </summary>
    public async Task<AppResult> DetachSnapshotAsync(
        Guid characterId,
        Guid worldId,
        Guid snapshotId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var ownershipError = await CheckStewardshipAsync(character, actingUserId, role, ct);
        if (ownershipError is not null)
        {
            return AppResult.Fail(ownershipError);
        }

        var snapshot = await _snapshotRepository.GetByIdAsync(snapshotId, ct);

        // A snapshot belonging to another character is "not there" for this caller, and delete
        // is idempotent by the repository contract, so both collapse to success.
        if (snapshot is not null && snapshot.CharacterId == characterId)
        {
            await _snapshotRepository.DeleteAsync(snapshotId, ct);
        }

        return AppResult.Success();
    }

    /// <summary>
    /// Replaces the character's written sheet. Steward or GM, via the same rule that governs
    /// renaming and deleting.
    ///
    /// Over-length input is refused, never truncated. Empty-after-trim is normalised to null so
    /// "never written" and "deliberately cleared" have one representation rather than two that
    /// render identically and compare differently.
    /// </summary>
    public async Task<AppResult<Character>> UpdateSheetAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        string? sheet,
        CancellationToken ct)
    {
        if (sheet is not null && sheet.Length > Character.MaxSheetChars)
        {
            return AppResult<Character>.Fail(new AppError(400, "validation_error",
                $"A character sheet must be {Character.MaxSheetChars} characters or fewer."));
        }

        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var ownershipError = await CheckStewardshipAsync(character, actingUserId, role, ct);
        if (ownershipError is not null)
        {
            return AppResult<Character>.Fail(ownershipError);
        }

        character.Sheet = string.IsNullOrWhiteSpace(sheet) ? null : sheet;
        character.SheetUpdatedAt = DateTimeOffset.UtcNow;
        character.UpdatedAt = character.SheetUpdatedAt.Value;
        character = await _characterRepository.UpdateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    /// <summary>
    /// Shares the sheet with the world, or stops sharing it.
    ///
    /// The steward only — deliberately narrower than <see cref="CheckStewardshipAsync"/>, which
    /// lets a GM manage any character. A GM may read a member's sheet; deciding who else reads
    /// it is not theirs to make. For a player who is not on Nornis there is no member to
    /// decide, and the GM stands in.
    /// </summary>
    public async Task<AppResult<Character>> SetSheetSharingAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        bool sharedWithParty,
        CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<Character>.Fail(new AppError(403, "insufficient_role", "Observers cannot share sheets."));
        }

        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        var player = await _playerRepository.GetByIdAsync(character.PlayerId, ct);

        if (!IsSteward(player, actingMember, role))
        {
            return AppResult<Character>.Fail(new AppError(403, "forbidden",
                "Only the player whose character this is can decide who reads its sheet."));
        }

        character.SheetSharedWithParty = sharedWithParty;
        character.UpdatedAt = DateTimeOffset.UtcNow;
        character = await _characterRepository.UpdateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult<IReadOnlyList<CharacterView>>> ListByWorldAsync(
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct,
        bool mineOnly = false)
    {
        var characters = await _characterRepository.ListByWorldAsync(worldId, ct);

        if (mineOnly)
        {
            var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
            var own = actingMember is null ? null : await _playerRepository.GetOrCreateByMemberAsync(actingMember, ct);
            characters = own is null ? [] : characters.Where(c => c.PlayerId == own.Id).ToList();
        }

        var views = await ProjectForReaderAsync(characters, worldId, actingUserId, role, ct);
        return AppResult<IReadOnlyList<CharacterView>>.Success(views);
    }

    public async Task<AppResult<Character>> UpdateAsync(UpdateCharacterCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole == WorldRole.Observer)
        {
            return AppResult<Character>.Fail(new AppError(403, "insufficient_role", "Observers cannot update characters."));
        }

        var character = await _characterRepository.GetByIdAsync(command.CharacterId, ct);

        if (character is null || character.WorldId != command.WorldId)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var ownershipError = await CheckStewardshipAsync(character, command.ActingUserId, command.ActingUserRole, ct);
        if (ownershipError is not null)
        {
            return AppResult<Character>.Fail(ownershipError);
        }

        if (command.Name is not null)
        {
            var nameError = ValidateName(command.Name);
            if (nameError is not null)
            {
                return AppResult<Character>.Fail(nameError);
            }

            character.Name = command.Name.Trim();
        }

        if (command.Description is not null)
        {
            character.Description = command.Description;
        }

        if (command.UnlinkArtifact)
        {
            character.ArtifactId = null;
        }
        else if (command.ArtifactId is { } artifactId)
        {
            var linkError = await ValidateArtifactLinkAsync(artifactId, command.WorldId, command.ActingUserRole, ct);
            if (linkError is not null)
            {
                return AppResult<Character>.Fail(linkError);
            }

            character.ArtifactId = artifactId;
        }

        character.UpdatedAt = DateTimeOffset.UtcNow;
        character = await _characterRepository.UpdateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult<Character>> ClaimAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<Character>.Fail(new AppError(403, "insufficient_role", "Observers cannot claim characters."));
        }

        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        if (actingMember is null)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "World membership not found."));
        }

        var own = await _playerRepository.GetOrCreateByMemberAsync(actingMember, ct);

        if (character.PlayerId == own.Id)
        {
            return AppResult<Character>.Success(character);
        }

        character.PlayerId = own.Id;
        character.UpdatedAt = DateTimeOffset.UtcNow;
        character = await _characterRepository.UpdateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult<Character>> MoveToPlayerAsync(Guid characterId, Guid worldId, Guid playerId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult<Character>.Fail(new AppError(403, "insufficient_role", "Observers cannot move characters."));
        }

        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var stewardshipError = await CheckStewardshipAsync(character, actingUserId, role, ct);
        if (stewardshipError is not null)
        {
            return AppResult<Character>.Fail(stewardshipError);
        }

        var target = await _playerRepository.GetByIdAsync(playerId, ct);
        if (target is null || target.WorldId != worldId)
        {
            return AppResult<Character>.Fail(new AppError(400, "invalid_player", "The target player does not belong to this world."));
        }

        if (character.PlayerId == target.Id)
        {
            return AppResult<Character>.Success(character);
        }

        character.PlayerId = target.Id;
        character.UpdatedAt = DateTimeOffset.UtcNow;
        character = await _characterRepository.UpdateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult> DeleteAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.Observer)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Observers cannot delete characters."));
        }

        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Character not found."));
        }

        var ownershipError = await CheckStewardshipAsync(character, actingUserId, role, ct);
        if (ownershipError is not null)
        {
            return AppResult.Fail(ownershipError);
        }

        await _characterRepository.DeleteAsync(characterId, ct);

        return AppResult.Success();
    }

    public async Task<IReadOnlyList<CharacterView>> ProjectForReaderAsync(
        IReadOnlyList<Character> characters,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct)
    {
        if (characters.Count == 0)
        {
            return [];
        }

        var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        var members = await _worldMemberRepository.ListByWorldAsync(worldId, ct);
        var players = (await _playerRepository.ListByWorldAsync(worldId, ct)).ToDictionary(p => p.Id);

        // One batch lookup for every linked artifact, then the reader's own filter over it —
        // the same VisibilityFilter the artifact queries apply in SQL, applied here in memory
        // to a handful of rows. An id that resolves to nothing (deleted, or somehow in another
        // world) is treated as invisible, which fails closed to "unlinked".
        var linkedIds = characters
            .Where(c => c.ArtifactId is not null)
            .Select(c => c.ArtifactId!.Value)
            .Distinct()
            .ToList();

        var filter = VisibilityFilter.ForRole(role, actingUserId);
        var visibleArtifactIds = linkedIds.Count == 0
            ? new HashSet<Guid>()
            : (await _artifactRepository.ListByIdsAsync(linkedIds, ct))
                .Where(a => a.WorldId == worldId && filter.CanSee(a.Visibility, a.CreatedByUserId))
                .Select(a => a.Id)
                .ToHashSet();

        return characters
            .Select(c =>
            {
                var player = players.GetValueOrDefault(c.PlayerId);
                return ToView(
                    c,
                    player,
                    PlayerNameFor(player, members),
                    actingMember,
                    role,
                    artifactVisible: c.ArtifactId is { } id && visibleArtifactIds.Contains(id));
            })
            .ToList();
    }

    private static CharacterView ToView(
        Character character,
        Player? player,
        string playerName,
        WorldMember? actingMember,
        WorldRole role,
        bool artifactVisible) =>
        new(
            Id: character.Id,
            WorldId: character.WorldId,
            PlayerId: character.PlayerId,
            PlayerName: playerName,
            Name: character.Name,
            Description: character.Description,
            ArtifactId: artifactVisible ? character.ArtifactId : null,
            ArtifactSlug: artifactVisible ? character.Artifact?.Slug : null,
            Slug: character.Slug,
            CampaignIds: character.CampaignCharacters.Select(cc => cc.CampaignId).ToList(),
            SheetUpdatedAt: CanReadSheet(character, player, actingMember, role) ? character.SheetUpdatedAt : null,
            CreatedAt: character.CreatedAt,
            UpdatedAt: character.UpdatedAt);

    /// <summary>
    /// A player row that cannot be resolved names nobody rather than throwing: the FK makes it
    /// impossible in the database, and the in-memory fakes should not need to seed one to read
    /// a character back.
    /// </summary>
    private static string PlayerNameFor(Player? player, IReadOnlyList<WorldMember> members) =>
        player is null ? "Unassigned" : PlayerDisplayName.For(player, members);

    /// <summary>
    /// The one rule for who may act on a character: the member its player is linked to, or —
    /// while the player is not on Nornis and there is no such member — any GM, who stands in
    /// for a player who is not here. Every ownership decision in this service goes through
    /// here; <c>CharacterServiceTests</c> holds that by widening it and watching two tests fail.
    /// </summary>
    private static bool IsSteward(Player? player, WorldMember? actingMember, WorldRole role) =>
        player?.WorldMemberId is { } linkedMemberId
            ? actingMember is not null && actingMember.Id == linkedMemberId
            : role == WorldRole.GM;

    /// <summary>
    /// The sheet's read gate: its steward, the GM, and the party once the steward has shared
    /// it. Everything that says anything about the sheet — its text, its timestamp, whether it
    /// is shared — is gated by this one rule, so there is one place for it to be wrong.
    /// </summary>
    private static bool CanReadSheet(Character character, Player? player, WorldMember? actingMember, WorldRole role) =>
        role == WorldRole.GM || IsSteward(player, actingMember, role) || character.SheetSharedWithParty;

    /// <summary>
    /// A member may manage their own player's characters; GMs may manage any character in
    /// the world.
    /// </summary>
    private async Task<AppError?> CheckStewardshipAsync(Character character, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.GM)
        {
            return null;
        }

        var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(character.WorldId, actingUserId, ct);
        var player = await _playerRepository.GetByIdAsync(character.PlayerId, ct);

        if (!IsSteward(player, actingMember, role))
        {
            return new AppError(403, "forbidden", "Only the player whose character this is, or a GM, can manage it.");
        }

        return null;
    }

    /// <summary>
    /// A character may only link to an existing Character-type artifact in the same
    /// world. Non-GMs also cannot link GM-only artifacts; the same error is returned
    /// for every failure so probing ids reveals nothing about hidden artifacts.
    /// </summary>
    private async Task<AppError?> ValidateArtifactLinkAsync(Guid artifactId, Guid worldId, WorldRole role, CancellationToken ct)
    {
        var invalid = new AppError(400, "invalid_artifact_link",
            "The linked artifact must be a Character artifact in this world.");

        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);

        if (artifact is null
            || artifact.WorldId != worldId
            || artifact.Type != ArtifactType.Character)
        {
            return invalid;
        }

        if (role != WorldRole.GM && artifact.Visibility == VisibilityScope.GMOnly)
        {
            return invalid;
        }

        return null;
    }

    private static AppError? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AppError(400, "validation_error", "Character name must not be empty or whitespace.");
        }

        if (name.Trim().Length > 200)
        {
            return new AppError(400, "validation_error", "Character name must be between 1 and 200 characters.");
        }

        return null;
    }
}
