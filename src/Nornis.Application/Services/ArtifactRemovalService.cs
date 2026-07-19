using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Removes a single artifact from canon — the per-artifact counterpart to source reprocess.
/// It tears down only what hangs off this artifact: its facts, the relationships touching it
/// (on either end), its map pins, and the provenance rows for all of those. Any player-character
/// link is cleared first (the Character→Artifact FK is NO ACTION, so a hard delete would
/// otherwise be rejected). The artifacts on the far side of each relationship are never touched.
/// </summary>
public class ArtifactRemovalService : IArtifactRemovalService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IMapPlacemarkRepository _mapPlacemarkRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ArtifactRemovalService> _logger;

    public ArtifactRemovalService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IMapPlacemarkRepository mapPlacemarkRepository,
        ICharacterRepository characterRepository,
        IUnitOfWork unitOfWork,
        ILogger<ArtifactRemovalService> logger)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _mapPlacemarkRepository = mapPlacemarkRepository;
        _characterRepository = characterRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AppResult<ArtifactRemovalPreview>> PreviewAsync(
        Guid worldId, Guid artifactId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var authResult = await AuthorizeAsync(worldId, artifactId, actingUserRole, ct);
        if (!authResult.IsSuccess)
        {
            return AppResult<ArtifactRemovalPreview>.Fail(authResult.Error!);
        }

        var artifact = authResult.Value!;
        var facts = await _artifactFactRepository.ListByArtifactAsync(artifactId, ct);
        var relationships = await _artifactRelationshipRepository.ListByArtifactAsync(artifactId, ct);
        var pins = await _mapPlacemarkRepository.ListByArtifactAsync(artifactId, ct);
        var characterLinks = await LinkedCharactersAsync(worldId, artifactId, ct);

        var relationshipDescriptions = new List<string>();
        foreach (var relationship in relationships)
        {
            var otherId = relationship.ArtifactAId == artifactId ? relationship.ArtifactBId : relationship.ArtifactAId;
            var other = await _artifactRepository.GetByIdAsync(otherId, ct);
            relationshipDescriptions.Add($"{relationship.Type}: {other?.Name ?? "(unknown)"}");
        }

        return AppResult<ArtifactRemovalPreview>.Success(new ArtifactRemovalPreview(
            ArtifactName: artifact.Name,
            ArtifactType: artifact.Type.ToString(),
            FactCount: facts.Count,
            Relationships: relationshipDescriptions,
            MapPinCount: pins.Count,
            CharacterLinksToClear: characterLinks.Count));
    }

    public async Task<AppResult> RemoveAsync(RemoveArtifactCommand command, CancellationToken ct)
    {
        var authResult = await AuthorizeAsync(command.WorldId, command.ArtifactId, command.ActingUserRole, ct);
        if (!authResult.IsSuccess)
        {
            return AppResult.Fail(authResult.Error!);
        }

        var facts = await _artifactFactRepository.ListByArtifactAsync(command.ArtifactId, ct);
        var relationships = await _artifactRelationshipRepository.ListByArtifactAsync(command.ArtifactId, ct);
        var linkedCharacters = await LinkedCharactersAsync(command.WorldId, command.ArtifactId, ct);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Clear player-character links first: the Character→Artifact FK is NO ACTION, so
            // a lingering link would otherwise reject the hard delete.
            foreach (var character in linkedCharacters)
            {
                character.ArtifactId = null;
                await _characterRepository.UpdateAsync(character, ct);
            }

            // Relationships before the artifact (their endpoint FKs are Restrict).
            foreach (var relationship in relationships)
            {
                await _artifactRelationshipRepository.DeleteAsync(relationship.Id, ct);
                await _sourceReferenceRepository.DeleteByTargetAsync(
                    SourceReferenceTargetType.ArtifactRelationship, relationship.Id, ct);
            }

            // Facts would cascade with the artifact, but delete them explicitly so their
            // provenance rows go too (SourceReference has no FK to its target).
            foreach (var fact in facts)
            {
                await _artifactFactRepository.DeleteAsync(fact.Id, ct);
                await _sourceReferenceRepository.DeleteByTargetAsync(
                    SourceReferenceTargetType.ArtifactFact, fact.Id, ct);
            }

            // Pins carry a loose ArtifactId with no cascade behind it.
            await _mapPlacemarkRepository.DeleteByArtifactAsync(command.ArtifactId, ct);

            await _sourceReferenceRepository.DeleteByTargetAsync(
                SourceReferenceTargetType.Artifact, command.ArtifactId, ct);

            await _artifactRepository.DeleteAsync(command.ArtifactId, ct);

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex,
                "Artifact removal cascade failed. ArtifactId={ArtifactId}, WorldId={WorldId}",
                command.ArtifactId, command.WorldId);
            return AppResult.Fail(new AppError(500, "transaction_failed",
                "Failed to remove the artifact. No changes were made."));
        }

        _logger.LogInformation(
            "Artifact removed from canon. ArtifactId={ArtifactId}, WorldId={WorldId}, " +
            "FactsDeleted={FactsDeleted}, RelationshipsDeleted={RelationshipsDeleted}, CharacterLinksCleared={CharacterLinksCleared}",
            command.ArtifactId, command.WorldId, facts.Count, relationships.Count, linkedCharacters.Count);

        return AppResult.Success();
    }

    /// <summary>GM-only gate: the artifact exists in this world and the actor is a GM.
    /// Removing shared canon (which can span sources) is a GM responsibility.</summary>
    private async Task<AppResult<Artifact>> AuthorizeAsync(
        Guid worldId, Guid artifactId, WorldRole actingUserRole, CancellationToken ct)
    {
        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);

        if (artifact is null || artifact.WorldId != worldId)
        {
            return AppResult<Artifact>.Fail(new AppError(404, "not_found", "Artifact not found."));
        }

        if (actingUserRole != WorldRole.GM)
        {
            return AppResult<Artifact>.Fail(new AppError(403, "insufficient_role",
                "Only a GM can remove an artifact from canon."));
        }

        return AppResult<Artifact>.Success(artifact);
    }

    private async Task<IReadOnlyList<Character>> LinkedCharactersAsync(Guid worldId, Guid artifactId, CancellationToken ct)
    {
        var characters = await _characterRepository.ListByWorldAsync(worldId, ct);
        return characters.Where(c => c.ArtifactId == artifactId).ToList();
    }
}
