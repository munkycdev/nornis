using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly IArtifactRepository _artifactRepository;

    public CharacterService(
        ICharacterRepository characterRepository,
        IWorldMemberRepository worldMemberRepository,
        IArtifactRepository artifactRepository)
    {
        _characterRepository = characterRepository;
        _worldMemberRepository = worldMemberRepository;
        _artifactRepository = artifactRepository;
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

        var ownerMemberId = actingMember.Id;

        if (command.ForWorldMemberId is not null && command.ForWorldMemberId != actingMember.Id)
        {
            if (command.ActingUserRole != WorldRole.GM)
            {
                return AppResult<Character>.Fail(new AppError(403, "forbidden", "Only GMs can create characters for other members."));
            }

            var targetMembers = await _worldMemberRepository.ListByWorldAsync(command.WorldId, ct);
            if (targetMembers.All(m => m.Id != command.ForWorldMemberId))
            {
                return AppResult<Character>.Fail(new AppError(400, "invalid_member", "The target member does not belong to this world."));
            }

            ownerMemberId = command.ForWorldMemberId.Value;
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
            WorldMemberId = ownerMemberId,
            Name = command.Name.Trim(),
            Description = command.Description,
            ArtifactId = command.ArtifactId,
            CreatedAt = now,
            UpdatedAt = now
        };

        character = await _characterRepository.CreateAsync(character, ct);

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult<Character>> GetByIdAsync(Guid characterId, Guid worldId, CancellationToken ct)
    {
        var character = await _characterRepository.GetByIdAsync(characterId, ct);

        if (character is null || character.WorldId != worldId)
        {
            return AppResult<Character>.Fail(new AppError(404, "not_found", "Character not found."));
        }

        return AppResult<Character>.Success(character);
    }

    public async Task<AppResult<IReadOnlyList<Character>>> ListByWorldAsync(Guid worldId, CancellationToken ct)
    {
        var characters = await _characterRepository.ListByWorldAsync(worldId, ct);
        return AppResult<IReadOnlyList<Character>>.Success(characters);
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

        var ownershipError = await CheckOwnershipAsync(character, command.ActingUserId, command.ActingUserRole, ct);
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

        if (character.WorldMemberId == actingMember.Id)
        {
            return AppResult<Character>.Success(character);
        }

        character.WorldMemberId = actingMember.Id;
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

        var ownershipError = await CheckOwnershipAsync(character, actingUserId, role, ct);
        if (ownershipError is not null)
        {
            return AppResult.Fail(ownershipError);
        }

        await _characterRepository.DeleteAsync(characterId, ct);

        return AppResult.Success();
    }

    /// <summary>
    /// A member may manage their own characters; GMs may manage any character in the world.
    /// </summary>
    private async Task<AppError?> CheckOwnershipAsync(Character character, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role == WorldRole.GM)
        {
            return null;
        }

        var actingMember = await _worldMemberRepository.GetByWorldAndUserAsync(character.WorldId, actingUserId, ct);

        if (actingMember is null || character.WorldMemberId != actingMember.Id)
        {
            return new AppError(403, "forbidden", "Only the owning member or a GM can manage this character.");
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
