using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

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

    public LibraryController(ILibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    [HttpPost("request-upload")]
    public async Task<IActionResult> RequestUpload(
        Guid worldId,
        [FromBody] RequestLibraryUploadRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!Enum.TryParse<LibraryDocumentKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return BadRequest(new ErrorResponse("invalid_kind", $"'{request.Kind}' is not a valid document kind."));
        }

        if (!Enum.TryParse<VisibilityScope>(request.Visibility, ignoreCase: true, out var visibility))
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
            return MapError(result.Error!);
        }

        return Ok(new LibraryUploadResponse(ToResponse(result.Value!.Document), result.Value.UploadUrl));
    }

    [HttpPost("{documentId:guid}/confirm")]
    public async Task<IActionResult> ConfirmUpload(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _libraryService.ConfirmUploadAsync(documentId, worldId, user.Id, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.ListAsync(worldId, member.Role, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToList())
            : MapError(result.Error!);
    }

    [HttpGet("{documentId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.GetByIdAsync(documentId, worldId, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> GetDownload(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.GetDownloadAsync(documentId, worldId, member.Role, ct);
        return result.IsSuccess
            ? Ok(new LibraryDownloadResponse(result.Value!.DownloadUrl, result.Value.FileName, result.Value.ContentType, result.Value.SizeBytes))
            : MapError(result.Error!);
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

        if (!Enum.TryParse<VisibilityScope>(request.Visibility, ignoreCase: true, out var visibility))
        {
            return BadRequest(new ErrorResponse("invalid_visibility", $"'{request.Visibility}' is not a valid visibility scope."));
        }

        var result = await _libraryService.SetVisibilityAsync(documentId, worldId, member.Role, visibility, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.DeleteAsync(documentId, worldId, user.Id, member.Role, ct);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpPost("{documentId:guid}/reindex")]
    public async Task<IActionResult> Reindex(Guid worldId, Guid documentId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();
        var result = await _libraryService.ReindexAsync(documentId, worldId, user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    private static LibraryDocumentResponse ToResponse(LibraryDocument d) => new(
        d.Id, d.WorldId, d.Title, d.FileName, d.ContentType, d.SizeBytes,
        d.Kind.ToString(), d.Visibility.ToString(), d.Status.ToString(),
        d.PageCount, d.ChunkCount, d.ErrorMessage, d.UploadedByUserId, d.CreatedAt, d.UpdatedAt);

    private IActionResult MapError(AppError error) => error.StatusCode switch
    {
        400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
        403 => StatusCode(403, new ErrorResponse(error.Code, error.Message)),
        404 => NotFound(new ErrorResponse(error.Code, error.Message)),
        409 => Conflict(new ErrorResponse(error.Code, error.Message)),
        _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message)),
    };
}
