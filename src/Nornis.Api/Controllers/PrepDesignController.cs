using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

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
    private readonly IAdventureDraftPlanService _plans;

    public PrepDesignController(IAdventureDesignService design, IAdventureDraftPlanService plans)
    {
        _design = design;
        _plans = plans;
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

    /// <summary>A split plan to show the GM; nothing is written.</summary>
    [HttpPost("draft/plan")]
    public async Task<IActionResult> PlanSplit(Guid worldId, Guid adventureId, [FromBody] PlanDraftRequest request, CancellationToken ct)
    {
        var result = await _plans.PlanAsync(new PlanDraftCommand(
            worldId, adventureId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role, request.UseLoremaster), ct);
        return result.IsSuccess
            ? Ok(new DraftPlanResponse(
                result.Value!.Rows.Select(r => new DraftPlanRowResponse(r.StartLine, r.Title, r.Kind.ToString(), r.Opening)).ToList(),
                result.Value.Source.ToString(), result.Value.Note, result.Value.DraftUpdatedAt))
            : result.Error!.ToActionResult();
    }

    /// <summary>Split into sections: cut by code at the plan's lines, checked so no word changes.</summary>
    [HttpPost("draft/split")]
    public async Task<IActionResult> Split(Guid worldId, Guid adventureId, [FromBody] SplitDraftRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<DraftSplitMode>(request.Mode ?? "", out var mode))
        {
            return BadRequest(new ErrorResponse("invalid_mode", $"'{request.Mode}' is not Replace or Append."));
        }

        var plan = new List<DraftSplitPoint>();
        foreach (var point in request.Plan ?? [])
        {
            if (!EnumParsing.TryParseDefined<AdventureSectionKind>(point.Kind ?? "", out var kind))
            {
                return BadRequest(new ErrorResponse("invalid_kind", $"'{point.Kind}' is not a kind of section."));
            }

            plan.Add(new DraftSplitPoint(point.StartLine, point.Title ?? "", kind));
        }

        return Adventure(await _design.SplitAsync(new SplitDraftCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, plan, mode, request.BasedOnUpdatedAt), ct));
    }

    /// <summary>Gather sections into the draft, as an undoable change.</summary>
    [HttpPost("draft/gather")]
    public async Task<IActionResult> Gather(Guid worldId, Guid adventureId, [FromBody] GatherDraftRequest request, CancellationToken ct) =>
        Draft(await _design.GatherAsync(new GatherDraftCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.BasedOnUpdatedAt), ct));

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
