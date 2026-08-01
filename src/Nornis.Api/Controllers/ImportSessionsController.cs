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
/// The campaign backlog import: paste the notes in, order them, then walk them oldest-first
/// one at a time. At most one non-terminal import exists per world; every operation is
/// GM-gated in the Application service.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/import-sessions")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class ImportSessionsController : ControllerBase
{
    private readonly IImportSessionService _importService;

    public ImportSessionsController(IImportSessionService importService)
    {
        _importService = importService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.CreateAsync(worldId, user.Id, member.Role, ct);
        return Respond(result);
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.GetCurrentAsync(worldId, user.Id, member.Role, ct);
        return Respond(result);
    }

    [HttpPost("{sessionId:guid}/items")]
    public async Task<IActionResult> AddItem(
        Guid worldId, Guid sessionId, [FromBody] AddImportNoteRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.AddItemAsync(new AddImportNoteCommand(
            worldId, sessionId, user.Id, member.Role, request.Title, request.Body, request.OccurredAt), ct);
        return Respond(result);
    }

    [HttpPut("{sessionId:guid}/items/order")]
    public async Task<IActionResult> Reorder(
        Guid worldId, Guid sessionId, [FromBody] ReorderImportItemsRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.ReorderAsync(
            worldId, sessionId, request.ItemIds ?? [], user.Id, member.Role, ct);
        return Respond(result);
    }

    [HttpGet("{sessionId:guid}/candidates")]
    public async Task<IActionResult> ListCandidates(Guid worldId, Guid sessionId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.ListCandidatesAsync(worldId, sessionId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(result.Value!.Select(c => new ImportCandidateResponse(
            c.SourceId,
            c.Title,
            c.Type.ToString(),
            c.StoryPosition,
            c.IsDated,
            c.ProcessingStatus.ToString(),
            c.ExistingReferenceCount,
            c.AlreadyStaged)).ToList());
    }

    [HttpPost("{sessionId:guid}/items/existing")]
    public async Task<IActionResult> AddExistingSources(
        Guid worldId, Guid sessionId, [FromBody] AddExistingSourcesRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.AddExistingSourcesAsync(
            worldId, sessionId, request?.SourceIds ?? [], user.Id, member.Role, ct);
        return Respond(result);
    }

    /// <summary>
    /// Drops a note from the run. The source is left alone — excluding a note from an import
    /// is a queue edit. Deleting the note itself is the opt-in below, and only ever applies to
    /// a note this import created.
    /// </summary>
    [HttpDelete("{sessionId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(
        Guid worldId, Guid sessionId, Guid itemId, [FromQuery] bool deleteNote, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = deleteNote
            ? await _importService.DeleteItemAsync(worldId, sessionId, itemId, user.Id, member.Role, ct)
            : await _importService.RemoveItemAsync(worldId, sessionId, itemId, user.Id, member.Role, ct);

        return Respond(result);
    }

    [HttpPost("{sessionId:guid}/start")]
    public async Task<IActionResult> Start(Guid worldId, Guid sessionId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.StartAsync(worldId, sessionId, user.Id, member.Role, ct);
        return Respond(result);
    }

    [HttpPost("{sessionId:guid}/advance")]
    public async Task<IActionResult> Advance(
        Guid worldId, Guid sessionId, [FromBody] AdvanceImportSessionRequest? request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.AdvanceAsync(
            worldId, sessionId, request?.SkipCurrent ?? false, request?.ExpectedItemId,
            user.Id, member.Role, ct);
        return Respond(result);
    }

    [HttpPost("{sessionId:guid}/abandon")]
    public async Task<IActionResult> Abandon(Guid worldId, Guid sessionId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _importService.AbandonAsync(worldId, sessionId, user.Id, member.Role, ct);
        return Respond(result);
    }

    private IActionResult Respond(AppResult<ImportSessionInfo> result)
    {
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToResponse(result.Value!));
    }

    private static ImportSessionResponse ToResponse(ImportSessionInfo info) =>
        new(info.Id,
            info.WorldId,
            info.Status.ToString(),
            info.CreatedAt,
            info.UpdatedAt,
            info.Items.Select(i => new ImportSessionItemResponse(
                i.Id,
                i.SourceId,
                i.Position,
                i.Skipped,
                i.Title,
                i.OccurredAt,
                i.ProcessingStatus.ToString(),
                i.State.ToString(),
                i.OpenProposalCount,
                i.CreatedByImport,
                i.ExistingReferenceCount)).ToList(),
            info.CurrentItemId,
            info.CurrentIndex,
            info.SettledCount);
}
