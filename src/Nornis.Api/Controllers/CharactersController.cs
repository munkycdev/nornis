using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/characters")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class CharactersController : ControllerBase
{
    private readonly ICharacterService _characterService;

    public CharactersController(ICharacterService characterService)
    {
        _characterService = characterService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid worldId,
        [FromBody] CreateCharacterRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new CreateCharacterCommand(
            WorldId: worldId,
            Name: request.Name,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Description: request.Description,
            ForWorldMemberId: request.WorldMemberId,
            ArtifactId: request.ArtifactId);

        var result = await _characterService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var character = result.Value!;
        return CreatedAtAction(nameof(GetById), new { worldId, characterId = character.Id }, await ToCharacterResponseAsync(character, ct));
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, [FromQuery] bool mine, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.ListByWorldAsync(worldId, user.Id, member.Role, ct);

        if (result.IsSuccess && mine)
        {
            return Ok(result.Value!
                .Where(c => c.WorldMemberId == member.Id)
                .Select(ToCharacterResponse)
                .ToList());
        }

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(result.Value!.Select(ToCharacterResponse).ToList());
    }

    [HttpGet("{characterId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid characterId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.GetByIdAsync(characterId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToCharacterResponse(result.Value!));
    }

    [HttpPut("{characterId:guid}")]
    public async Task<IActionResult> Update(
        Guid worldId,
        Guid characterId,
        [FromBody] UpdateCharacterRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new UpdateCharacterCommand(
            CharacterId: characterId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Name: request.Name,
            Description: request.Description,
            ArtifactId: request.ArtifactId,
            UnlinkArtifact: request.UnlinkArtifact);

        var result = await _characterService.UpdateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(await ToCharacterResponseAsync(result.Value!, ct));
    }

    /// <summary>Transfers ownership of the character to the calling member.</summary>
    [HttpPost("{characterId:guid}/claim")]
    public async Task<IActionResult> Claim(Guid worldId, Guid characterId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.ClaimAsync(characterId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(await ToCharacterResponseAsync(result.Value!, ct));
    }

    [HttpDelete("{characterId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid characterId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.DeleteAsync(characterId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return NoContent();
    }

    [HttpGet("{characterId:guid}/dossier")]
    public async Task<IActionResult> GetDossier(Guid worldId, Guid characterId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.GetDossierAsync(characterId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var dossier = result.Value!;

        return Ok(new CharacterDossierResponse(
            Character: ToCharacterResponse(dossier.Character),
            OwnerDisplayName: dossier.OwnerDisplayName,
            CampaignNames: dossier.CampaignNames,
            Record: dossier.Record is null ? null : new CharacterRecordResponse(
                ArtifactId: dossier.Record.ArtifactId,
                ArtifactName: dossier.Record.ArtifactName,
                Summary: dossier.Record.Summary,
                Facts: dossier.Record.Facts.Select(ArtifactsController.ToFactResponse).ToList(),
                TotalFactCount: dossier.Record.TotalFactCount,
                Groups: dossier.Record.Groups.Select(g => new CharacterRecordGroupResponse(
                    Type: g.Type.ToString(),
                    Artifacts: g.Artifacts.Select(ArtifactsController.ToConnectedResponse).ToList(),
                    TotalCount: g.TotalCount)).ToList()),
            Sheet: dossier.Sheet,
            SheetSharedWithParty: dossier.SheetSharedWithParty,
            CanEditSheet: dossier.CanEditSheet,
            CanShareSheet: dossier.CanShareSheet,
            Snapshots: dossier.Snapshots.Select(s => new CharacterSnapshotResponse(
                Id: s.Id,
                SourceId: s.SourceId,
                SourceTitle: s.SourceTitle,
                AsOf: s.AsOf,
                Note: s.Note)).ToList()));
    }

    [HttpPost("{characterId:guid}/snapshots")]
    public async Task<IActionResult> AttachSnapshot(
        Guid worldId,
        Guid characterId,
        [FromBody] AttachCharacterSnapshotRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.AttachSnapshotAsync(
            characterId, worldId, request.SourceId, request.AsOf, request.Note, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        // No body: a snapshot is only meaningful with its source's title beside it, and that
        // title is resolved through the reader's visibility on the dossier. Returning a
        // titleless half of one here would be a second, worse shape of the same thing.
        return NoContent();
    }

    [HttpDelete("{characterId:guid}/snapshots/{snapshotId:guid}")]
    public async Task<IActionResult> DetachSnapshot(
        Guid worldId,
        Guid characterId,
        Guid snapshotId,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.DetachSnapshotAsync(
            characterId, worldId, snapshotId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return NoContent();
    }

    [HttpPut("{characterId:guid}/sheet")]
    public async Task<IActionResult> UpdateSheet(
        Guid worldId,
        Guid characterId,
        [FromBody] UpdateCharacterSheetRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.UpdateSheetAsync(
            characterId, worldId, user.Id, member.Role, request.Sheet, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(await ToCharacterResponseAsync(result.Value!, ct));
    }

    [HttpPut("{characterId:guid}/sheet/sharing")]
    public async Task<IActionResult> SetSheetSharing(
        Guid worldId,
        Guid characterId,
        [FromBody] SetCharacterSheetSharingRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _characterService.SetSheetSharingAsync(
            characterId, worldId, user.Id, member.Role, request.SharedWithParty, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(await ToCharacterResponseAsync(result.Value!, ct));
    }

    /// <summary>
    /// A mutation's result leaves through the same reader projection as every read, so the
    /// actor is told no more about the character they just changed than a fresh GET would
    /// tell them. There is no entity-to-response mapping in this controller on purpose.
    /// </summary>
    private async Task<CharacterResponse> ToCharacterResponseAsync(Character character, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var views = await _characterService.ProjectForReaderAsync([character], character.WorldId, user.Id, member.Role, ct);
        return ToCharacterResponse(views[0]);
    }

    internal static CharacterResponse ToCharacterResponse(CharacterView character)
    {
        return new CharacterResponse(
            Id: character.Id,
            WorldId: character.WorldId,
            WorldMemberId: character.WorldMemberId,
            Name: character.Name,
            Description: character.Description,
            ArtifactId: character.ArtifactId,
            CampaignIds: character.CampaignIds,
            SheetUpdatedAt: character.SheetUpdatedAt,
            CreatedAt: character.CreatedAt,
            UpdatedAt: character.UpdatedAt);
    }
}
