using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// Prep's design mode (feature 37): the draft, the table brief and the source books. GM-only,
/// with the rule in <see cref="IAdventureDesignService"/>; this controller only translates.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/prep/{adventureId:guid}")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class PrepDesignController : ControllerBase
{
    private readonly IAdventureDesignService _design;

    public PrepDesignController(IAdventureDesignService design)
    {
        _design = design;
    }

    [HttpPut("draft")]
    public async Task<IActionResult> SaveDraft(
        Guid worldId, Guid adventureId, [FromBody] SaveAdventureDraftRequest request, CancellationToken ct) =>
        Draft(await _design.SaveDraftAsync(new SaveDraftCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.Markdown, request.BasedOnUpdatedAt), ct));

    [HttpPost("draft/undo")]
    public async Task<IActionResult> UndoDraft(Guid worldId, Guid adventureId, CancellationToken ct) =>
        Draft(await _design.UndoDraftAsync(new UndoDraftCommand(worldId, adventureId, HttpContext.GetWorldMember().Role), ct));

    /// <summary>Applies one suggested edit from a saved answer; 409 <c>edit_no_longer_fits</c> when its passage moved.</summary>
    [HttpPost("draft/edits")]
    public async Task<IActionResult> ApplyEdit(
        Guid worldId, Guid adventureId, [FromBody] ApplyDraftEditRequest request, CancellationToken ct) =>
        Draft(await _design.ApplyEditAsync(new ApplyDraftEditCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.MessageId, request.Index, request.BasedOnUpdatedAt), ct));

    /// <summary>Put in draft: a whole saved answer replaces the draft or goes after it, as an undoable change.</summary>
    [HttpPost("draft/put")]
    public async Task<IActionResult> PutInDraft(
        Guid worldId, Guid adventureId, [FromBody] PutInDraftRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<PutInSectionMode>(request.Mode ?? "", out var mode))
        {
            return BadRequest(new ErrorResponse("invalid_mode", $"'{request.Mode}' is not Replace or Append."));
        }

        return Draft(await _design.PutInDraftAsync(new PutInDraftCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.MessageId, mode, request.BasedOnUpdatedAt), ct));
    }

    [HttpPut("brief")]
    public async Task<IActionResult> SetBrief(
        Guid worldId, Guid adventureId, [FromBody] SetTableBriefRequest request, CancellationToken ct) =>
        Adventure(await _design.SetTableBriefAsync(new SetTableBriefCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.Brief), ct));

    [HttpPut("source-books")]
    public async Task<IActionResult> SetSourceBooks(
        Guid worldId, Guid adventureId, [FromBody] SetSourceBooksRequest request, CancellationToken ct) =>
        Adventure(await _design.SetSourceBooksAsync(new SetSourceBooksCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.DocumentIds ?? []), ct));

    private IActionResult Draft(AppResult<AdventureDraftState> result) =>
        result.IsSuccess
            ? Ok(new AdventureDraftResponse(result.Value!.Markdown ?? "", result.Value.UpdatedAt ?? DateTimeOffset.UtcNow, result.Value.RevisionCount))
            : result.Error!.ToActionResult();

    private IActionResult Adventure(AppResult<AdventureView> result) =>
        result.IsSuccess ? Ok(PrepController.ToResponse(result.Value!)) : result.Error!.ToActionResult();
}
