using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// Prep: the GM's Adventures. Every route is GM-only, and the rule lives in
/// <see cref="IAdventureService"/>; this controller only translates.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/prep")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class PrepController : ControllerBase
{
    private readonly IAdventureService _adventures;

    public PrepController(IAdventureService adventures)
    {
        _adventures = adventures;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var result = await _adventures.ListAsync(worldId, HttpContext.GetWorldMember().Role, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(a => new AdventureSummaryResponse(
                a.Id, a.Title, a.Slug, a.CampaignId, a.SectionCount, a.FilledSectionCount, a.LastPrintedDocumentId, a.UpdatedAt)).ToList())
            : result.Error!.ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid worldId, [FromBody] CreateAdventureRequest request, CancellationToken ct)
    {
        var result = await _adventures.CreateAsync(new CreateAdventureCommand(
            worldId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role,
            request.Title, request.Pitch, request.CampaignId), ct);
        return Respond(worldId, result);
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(Guid worldId, [FromBody] ImportAdventureRequest request, CancellationToken ct)
    {
        var files = (request.Files ?? []).Select(f => new MarkdownFile(f.Name ?? "", f.Markdown ?? "")).ToList();
        var result = await _adventures.ImportAsync(new ImportAdventureCommand(
            worldId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role, files, request.CampaignId), ct);
        return Respond(worldId, result);
    }

    /// <summary>One Adventure with its sections, by slug or id.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(Guid worldId, string key, CancellationToken ct) =>
        Respond(await _adventures.GetByKeyAsync(worldId, key, HttpContext.GetWorldMember().Role, ct));

    [HttpPut("{adventureId:guid}")]
    public async Task<IActionResult> Update(
        Guid worldId, Guid adventureId, [FromBody] UpdateAdventureRequest request, CancellationToken ct)
    {
        var result = await _adventures.UpdateAsync(new UpdateAdventureCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.Title, request.Pitch, request.CampaignId), ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return await Fetch(worldId, adventureId, ct);
    }

    [HttpDelete("{adventureId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var result = await _adventures.DeleteAsync(
            new DeleteAdventureCommand(worldId, adventureId, HttpContext.GetWorldMember().Role), ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    [HttpPut("{adventureId:guid}/sections/{sectionId:guid}")]
    public async Task<IActionResult> SaveSection(
        Guid worldId, Guid adventureId, Guid sectionId, [FromBody] SaveAdventureSectionRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<AdventureSectionKind>(request.Kind ?? "", out var kind))
        {
            return BadRequest(new ErrorResponse("invalid_kind", $"'{request.Kind}' is not a section kind."));
        }

        var result = await _adventures.SaveSectionAsync(new SaveSectionCommand(
            worldId, adventureId, sectionId, HttpContext.GetWorldMember().Role,
            kind, request.Title, request.BodyMarkdown ?? "", request.BasedOnUpdatedAt), ct);
        return result.IsSuccess ? Ok(ToSavedSection(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpPost("{adventureId:guid}/sections")]
    public async Task<IActionResult> AddSection(
        Guid worldId, Guid adventureId, [FromBody] AddAdventureSectionRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<AdventureSectionKind>(request.Kind ?? "", out var kind))
        {
            return BadRequest(new ErrorResponse("invalid_kind", $"'{request.Kind}' is not a section kind."));
        }

        return Respond(await _adventures.AddSectionAsync(new AddSectionCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, kind, request.Title, request.AfterSectionId), ct));
    }

    [HttpDelete("{adventureId:guid}/sections/{sectionId:guid}")]
    public async Task<IActionResult> DeleteSection(Guid worldId, Guid adventureId, Guid sectionId, CancellationToken ct) =>
        Respond(await _adventures.DeleteSectionAsync(
            new DeleteSectionCommand(worldId, adventureId, sectionId, HttpContext.GetWorldMember().Role), ct));

    [HttpPut("{adventureId:guid}/order")]
    public async Task<IActionResult> Reorder(
        Guid worldId, Guid adventureId, [FromBody] ReorderAdventureSectionsRequest request, CancellationToken ct) =>
        Respond(await _adventures.ReorderAsync(new ReorderSectionsCommand(
            worldId, adventureId, HttpContext.GetWorldMember().Role, request.SectionIds ?? []), ct));

    [HttpPost("{adventureId:guid}/sections/{sectionId:guid}/undo")]
    public async Task<IActionResult> Undo(Guid worldId, Guid adventureId, Guid sectionId, CancellationToken ct)
    {
        var result = await _adventures.UndoAsync(
            new UndoSectionCommand(worldId, adventureId, sectionId, HttpContext.GetWorldMember().Role), ct);
        // The whole Adventure back: the undo changed the section and its revision count.
        return result.IsSuccess ? await Fetch(worldId, adventureId, ct) : result.Error!.ToActionResult();
    }

    private async Task<IActionResult> Fetch(Guid worldId, Guid adventureId, CancellationToken ct) =>
        Respond(await _adventures.GetAsync(worldId, adventureId, HttpContext.GetWorldMember().Role, ct));

    private IActionResult Respond(Guid worldId, AppResult<AdventureView> result) =>
        result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { worldId, key = result.Value!.Adventure.Id.ToString() }, ToResponse(result.Value))
            : result.Error!.ToActionResult();

    private IActionResult Respond(AppResult<AdventureView> result) =>
        result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();

    private static AdventureResponse ToResponse(AdventureView view)
    {
        var a = view.Adventure;
        return new AdventureResponse(
            a.Id, a.WorldId, a.CampaignId, a.Title, a.Slug, a.Pitch, a.LastPrintedDocumentId, a.CreatedAt, a.UpdatedAt,
            a.Sections.OrderBy(s => s.SortOrder)
                .Select(s => ToSection(s, view.RevisionCounts.TryGetValue(s.Id, out var n) ? n : 0))
                .ToList());
    }

    private static AdventureSectionResponse ToSection(AdventureSection s, int revisionCount) =>
        new(s.Id, s.Kind.ToString(), s.Title, s.BodyMarkdown, s.SortOrder, s.UpdatedAt, revisionCount);

    private static SavedAdventureSectionResponse ToSavedSection(AdventureSection s) =>
        new(s.Id, s.Kind.ToString(), s.Title, s.BodyMarkdown, s.SortOrder, s.UpdatedAt);
}
