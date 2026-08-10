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
        return CreatedAtAction(nameof(GetById), new { worldId, characterId = character.Id }, ToCharacterResponse(character));
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, [FromQuery] bool mine, CancellationToken ct)
    {
        var result = await _characterService.ListByWorldAsync(worldId, ct);

        if (result.IsSuccess && mine)
        {
            var member = HttpContext.GetWorldMember();
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
        var result = await _characterService.GetByIdAsync(characterId, worldId, ct);

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

        return Ok(ToCharacterResponse(result.Value!));
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

        return Ok(ToCharacterResponse(result.Value!));
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
                    TotalCount: g.TotalCount)).ToList())));
    }

    internal static CharacterResponse ToCharacterResponse(Character character)
    {
        return new CharacterResponse(
            Id: character.Id,
            WorldId: character.WorldId,
            WorldMemberId: character.WorldMemberId,
            Name: character.Name,
            Description: character.Description,
            ArtifactId: character.ArtifactId,
            CampaignIds: character.CampaignCharacters.Select(cc => cc.CampaignId).ToList(),
            CreatedAt: character.CreatedAt,
            UpdatedAt: character.UpdatedAt);
    }
}
