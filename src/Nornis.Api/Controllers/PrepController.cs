using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private readonly IAdventureDraftingService _drafting;
    private readonly IAdventureExportService _export;
    private readonly IAdventureConversationService _conversation;

    public PrepController(
        IAdventureService adventures, IAdventureDraftingService drafting, IAdventureExportService export,
        IAdventureConversationService conversation)
    {
        _adventures = adventures;
        _drafting = drafting;
        _export = export;
        _conversation = conversation;
    }

    /// <summary>
    /// The month's Prep spend. Outside <c>prep/</c> on purpose: every literal under it is also a
    /// slug some Adventure could have, and <c>prep/budget</c> would hide one titled "Budget".
    /// </summary>
    [HttpGet("~/api/worlds/{worldId:guid}/prep-budget")]
    public async Task<IActionResult> Budget(Guid worldId, CancellationToken ct)
    {
        var result = await _drafting.GetBudgetAsync(worldId, HttpContext.GetWorldMember().Role, ct);
        return result.IsSuccess
            ? Ok(new PrepBudgetResponse(result.Value!.SpentThisMonthUsd, result.Value.MonthlyBudgetUsd, result.Value.IsExceeded))
            : result.Error!.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var result = await _adventures.ListAsync(worldId, HttpContext.GetWorldMember().Role, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(a => new AdventureSummaryResponse(
                a.Id, a.Title, a.Slug, a.CampaignId, a.SectionCount, a.FilledSectionCount, a.LastPrintedDocumentId, a.UpdatedAt,
                a.IsExported)).ToList())
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

    [HttpGet("{adventureId:guid}/print")]
    public async Task<IActionResult> Print(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var result = await _adventures.GetPrintAsync(worldId, adventureId, HttpContext.GetWorldMember().Role, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var print = result.Value!;
        return Ok(new AdventurePrintResponse(print.Adventure.Id, print.Adventure.Title, print.Adventure.Slug, print.Html, print.LibraryTitle));
    }

    /// <summary>
    /// Export to Nornis: the whole Adventure, or the Chapter named, filed as a new GM-only
    /// source and queued for extraction. The answer names the source so the page can link to it.
    /// </summary>
    [HttpPost("{adventureId:guid}/export")]
    public async Task<IActionResult> Export(
        Guid worldId, Guid adventureId, [FromBody] ExportAdventureRequest? request, CancellationToken ct)
    {
        var result = await _export.ExportAsync(new ExportAdventureCommand(
            worldId, adventureId, request?.ChapterSectionId, HttpContext.GetNornisUser().Id,
            HttpContext.GetWorldMember().Role), ct);
        return result.IsSuccess
            ? Ok(new AdventureExportResponse(result.Value!.Id, result.Value.Title, result.Value.Slug))
            : result.Error!.ToActionResult();
    }

    [HttpPost("{adventureId:guid}/sections/{sectionId:guid}/undo")]
    public async Task<IActionResult> Undo(Guid worldId, Guid adventureId, Guid sectionId, CancellationToken ct)
    {
        var result = await _adventures.UndoAsync(
            new UndoSectionCommand(worldId, adventureId, sectionId, HttpContext.GetWorldMember().Role), ct);
        // The whole Adventure back: the undo changed the section and its revision count.
        return result.IsSuccess ? await Fetch(worldId, adventureId, ct) : result.Error!.ToActionResult();
    }

    /// <summary>
    /// Draft or revise one section, as server-sent events: a <c>delta</c> per piece of text, then
    /// <c>done</c> with the saved section or <c>error</c> when nothing was saved. Every refusal
    /// that can come before the model starts (role, a stale stamp, a draft already running, the
    /// budget) keeps its status code; only what happens after the first byte rides in-band.
    /// </summary>
    [HttpPost("{adventureId:guid}/sections/{sectionId:guid}/draft")]
    public async Task<IActionResult> Draft(
        Guid worldId, Guid adventureId, Guid sectionId, [FromBody] DraftAdventureSectionRequest request, CancellationToken ct)
    {
        var result = await _drafting.DraftAsync(new DraftSectionCommand(
            worldId, adventureId, sectionId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role,
            request.BasedOnUpdatedAt, request.Instruction), ct);
        return result.IsSuccess
            ? new ServerSentEventsActionResult(ToServerSentEvents(result.Value!))
            : result.Error!.ToActionResult();
    }

    /// <summary>The Adventure's side conversation, oldest first; empty before the first message.</summary>
    [HttpGet("{adventureId:guid}/conversation")]
    public async Task<IActionResult> Conversation(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var result = await _conversation.GetAsync(worldId, adventureId, HttpContext.GetWorldMember().Role, ct);
        return result.IsSuccess
            ? Ok(new ConversationResponse(result.Value!.Messages.Select(ToMessage).ToList(), result.Value.IsFull))
            : result.Error!.ToActionResult();
    }

    /// <summary>
    /// One turn, as server-sent events under the draft's names: a <c>delta</c> per piece of the
    /// answer, then <c>done</c> with the saved answer or <c>error</c> when none was saved. The
    /// GM's message is saved before the model starts. Refusals before the first byte keep their
    /// status codes, as a draft's do.
    /// </summary>
    [HttpPost("{adventureId:guid}/conversation/messages")]
    public async Task<IActionResult> SendMessage(
        Guid worldId, Guid adventureId, [FromBody] SendConversationMessageRequest request, CancellationToken ct)
    {
        var result = await _conversation.SendAsync(new SendConversationMessageCommand(
            worldId, adventureId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role,
            request.Text ?? "", request.SectionId), ct);
        return result.IsSuccess
            ? new ServerSentEventsActionResult(ToServerSentEvents(result.Value!))
            : result.Error!.ToActionResult();
    }

    /// <summary>Clear conversation: every message goes. The Adventure is untouched.</summary>
    [HttpDelete("{adventureId:guid}/conversation")]
    public async Task<IActionResult> ClearConversation(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var result = await _conversation.ClearAsync(worldId, adventureId, HttpContext.GetWorldMember().Role, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    /// <summary>Put one saved answer into a section, as an undoable revision. The answer is the section with its Undo count.</summary>
    [HttpPost("{adventureId:guid}/sections/{sectionId:guid}/put")]
    public async Task<IActionResult> PutInSection(
        Guid worldId, Guid adventureId, Guid sectionId, [FromBody] PutInSectionRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<PutInSectionMode>(request.Mode ?? "", out var mode))
        {
            return BadRequest(new ErrorResponse("invalid_mode", $"'{request.Mode}' is not Replace or Append."));
        }

        var result = await _conversation.PutInSectionAsync(new PutInSectionCommand(
            worldId, adventureId, sectionId, HttpContext.GetWorldMember().Role,
            request.MessageId, mode, request.BasedOnUpdatedAt), ct);
        return result.IsSuccess
            ? Ok(ToSection(result.Value!.Section, result.Value.RevisionCount))
            : result.Error!.ToActionResult();
    }

    private static async IAsyncEnumerable<SseItem<string>> ToServerSentEvents(
        IAsyncEnumerable<ConversationEvent> events, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var e in events.WithCancellation(ct))
        {
            yield return e switch
            {
                ConversationEvent.Delta delta => Event(DraftStreamEventNames.Delta, new DraftStreamDeltaResponse(delta.Text)),
                ConversationEvent.Completed done => Event(DraftStreamEventNames.Done, ToMessage(done.Answer)),
                ConversationEvent.Failed failed => Event(DraftStreamEventNames.Error, failed.Error.ToErrorResponse()),
                _ => throw new UnreachableException($"Unhandled conversation event {e.GetType().Name}."),
            };
        }
    }

    private static ConversationMessageResponse ToMessage(ConversationMessage m) =>
        new(m.Id, m.Role.ToString(), m.Content, m.Model, m.CreatedAt);

    private static async IAsyncEnumerable<SseItem<string>> ToServerSentEvents(
        IAsyncEnumerable<DraftEvent> events, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var e in events.WithCancellation(ct))
        {
            yield return e switch
            {
                DraftEvent.Delta delta => Event(DraftStreamEventNames.Delta, new DraftStreamDeltaResponse(delta.Text)),
                DraftEvent.Completed done => Event(DraftStreamEventNames.Done, ToSection(done.Section, done.RevisionCount)),
                DraftEvent.Failed failed => Event(DraftStreamEventNames.Error, failed.Error.ToErrorResponse()),
                _ => throw new UnreachableException($"Unhandled draft event {e.GetType().Name}."),
            };
        }
    }

    private static SseItem<string> Event<T>(string eventName, T payload) =>
        new(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), eventName);

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
        new(s.Id, s.Kind.ToString(), s.Title, s.BodyMarkdown, s.SortOrder, s.UpdatedAt, revisionCount,
            !AdventureSkeleton.IsUnfilled(s.BodyMarkdown));

    private static SavedAdventureSectionResponse ToSavedSection(AdventureSection s) =>
        new(s.Id, s.Kind.ToString(), s.Title, s.BodyMarkdown, s.SortOrder, s.UpdatedAt,
            !AdventureSkeleton.IsUnfilled(s.BodyMarkdown));
}
