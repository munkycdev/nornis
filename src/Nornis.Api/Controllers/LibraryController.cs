using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Controllers;

/// <summary>
/// Library documents — immutable reference files (sourcebooks, maps, handouts). Uploads use
/// the SAS handshake: request-upload returns a short-lived write URL, the browser PUTs the
/// bytes straight to blob storage, and confirm verifies arrival (queueing PDF indexing).
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/library")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class LibraryController : ControllerBase
{
    private readonly ILibraryService _libraryService;
    private readonly ILibraryExcerptService _excerptService;

    public LibraryController(ILibraryService libraryService, ILibraryExcerptService excerptService)
    {
        _libraryService = libraryService;
        _excerptService = excerptService;
    }

    [HttpPost("request-upload")]
    public async Task<IActionResult> RequestUpload(
        Guid worldId,
        [FromBody] RequestLibraryUploadRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!EnumParsing.TryParseDefined<LibraryDocumentKind>(request.Kind, out var kind))
        {
            return BadRequest(new ErrorResponse("invalid_kind", $"'{request.Kind}' is not a valid document kind."));
        }

        if (!EnumParsing.TryParseDefined<VisibilityScope>(request.Visibility, out var visibility))
        {
            return BadRequest(new ErrorResponse("invalid_visibility", $"'{request.Visibility}' is not a valid visibility scope."));
        }

        var command = new RequestLibraryUploadCommand(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Title: request.Title,
            FileName: request.FileName,
            ContentType: request.ContentType,
            SizeBytes: request.SizeBytes,
            Kind: kind,
            Visibility: visibility);

        var result = await _libraryService.RequestUploadAsync(command, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(new LibraryUploadResponse(ToResponse(result.Value!.Document), result.Value.UploadUrl));
    }

    [HttpPost("{documentId:guid}/confirm")]
    public async Task<IActionResult> ConfirmUpload(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _libraryService.ConfirmUploadAsync(documentId, worldId, user.Id, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.ListAsync(worldId, member.Role, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToList())
            : result.Error!.ToActionResult();
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> GetByKey(
        Guid worldId, string key, [FromServices] ISlugResolver slugResolver, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var resolved = await slugResolver.ResolveKeyAsync<LibraryDocument>(worldId, key, "Library document", ct);
        if (!resolved.IsSuccess)
        {
            return resolved.Error!.ToActionResult();
        }

        var result = await _libraryService.GetByIdAsync(resolved.Value, worldId, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> GetDownload(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.GetDownloadAsync(documentId, worldId, member.Role, ct);
        return result.IsSuccess
            ? Ok(new LibraryDownloadResponse(result.Value!.DownloadUrl, result.Value.FileName, result.Value.ContentType, result.Value.SizeBytes))
            : result.Error!.ToActionResult();
    }

    /// <summary>GM-only: moves a document between the party shelf and the GM's.</summary>
    [HttpPut("{documentId:guid}/visibility")]
    public async Task<IActionResult> SetVisibility(
        Guid worldId,
        Guid documentId,
        [FromBody] SetLibraryVisibilityRequest request,
        CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();

        if (!EnumParsing.TryParseDefined<VisibilityScope>(request.Visibility, out var visibility))
        {
            return BadRequest(new ErrorResponse("invalid_visibility", $"'{request.Visibility}' is not a valid visibility scope."));
        }

        var result = await _libraryService.SetVisibilityAsync(documentId, worldId, member.Role, visibility, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    /// <summary>GM-only: retitles a document.</summary>
    [HttpPut("{documentId:guid}/title")]
    public async Task<IActionResult> Rename(
        Guid worldId,
        Guid documentId,
        [FromBody] RenameLibraryDocumentRequest request,
        CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.RenameAsync(documentId, worldId, member.Role, request.Title, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    /// <summary>GM-only: removes the document, its file, and its indexed passages.</summary>
    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.DeleteAsync(documentId, worldId, member.Role, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    [HttpPost("{documentId:guid}/reindex")]
    public async Task<IActionResult> Reindex(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.ReindexAsync(documentId, worldId, user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    /// <summary>GM-only: passages across the GM's shelves that match a codex entry (or a free query).</summary>
    [HttpPost("excerpts/search")]
    public async Task<IActionResult> SearchExcerpts(
        Guid worldId,
        [FromBody] SearchLibraryExcerptsRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _excerptService.SearchAsync(
            new SearchLibraryExcerptsCommand(worldId, user.Id, member.Role, request.ArtifactId, request.Query), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(c => new LibraryExcerptCandidateResponse(c.ChunkId, c.DocumentId, c.DocumentTitle, c.Page, c.Text)).ToList())
            : result.Error!.ToActionResult();
    }

    /// <summary>GM-only: files chosen passages of a document as an excerpt source, queued for extraction.</summary>
    [HttpPost("{documentId:guid}/excerpts")]
    public async Task<IActionResult> FileExcerpt(
        Guid worldId,
        Guid documentId,
        [FromBody] FileLibraryExcerptRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _excerptService.FileAsync(
            new FileLibraryExcerptCommand(
                worldId, user.Id, member.Role, documentId,
                request.ChunkIds ?? [], request.PageFrom, request.PageTo, request.ArtifactId), ct);

        return result.IsSuccess
            ? Ok(new LibraryExcerptFiledResponse(result.Value!.Id, result.Value.Title, result.Value.ProcessingStatus.ToString(), result.Value.Slug))
            : result.Error!.ToActionResult();
    }

    private static LibraryDocumentResponse ToResponse(LibraryDocument d) => new(
        d.Id, d.WorldId, d.Title, d.FileName, d.ContentType, d.SizeBytes,
        d.Kind.ToString(), d.Visibility.ToString(), d.Status.ToString(),
        d.PageCount, d.ChunkCount, d.ErrorMessage, d.UploadedByUserId, d.CreatedAt, d.UpdatedAt, d.Slug);
}
