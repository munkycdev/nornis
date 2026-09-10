using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Application.Services;
using Nornis.Domain.Entities;

namespace Nornis.Api.Controllers;

/// <summary>
/// The party shelf, opened by a shelf link — the anonymous half of shelf links, for the person
/// at the table who is not on Nornis. Authorizes on the code alone: there is no membership to
/// filter on, which is the point. Rate-limited with the public surface. Deliberately not
/// output-cached: the response is a function of a secret, and a revocation has to land on the
/// next request rather than at the end of a cache window. Minting and revoking live on the
/// world-scoped <see cref="ShelfLinksController"/>.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public")]
[Route("api/shelf/{code}")]
public class ShelfController : ControllerBase
{
    private readonly IShelfLinkService _shelfLinks;

    public ShelfController(IShelfLinkService shelfLinks)
    {
        _shelfLinks = shelfLinks;
    }

    [HttpGet("")]
    public async Task<IActionResult> Open(string code, CancellationToken ct)
    {
        var result = await _shelfLinks.OpenAsync(code, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var shelf = result.Value!;
        return Ok(new ShelfResponse(
            shelf.WorldName,
            shelf.PlayerName,
            shelf.Documents.Select(ToDocumentResponse).ToList()));
    }

    [HttpGet("documents/{documentId:guid}/download")]
    public async Task<IActionResult> Download(string code, Guid documentId, CancellationToken ct)
    {
        var result = await _shelfLinks.DownloadAsync(code, documentId, ct);
        return result.IsSuccess
            ? Ok(new LibraryDownloadResponse(result.Value!.DownloadUrl, result.Value.FileName, result.Value.ContentType, result.Value.SizeBytes))
            : result.Error!.ToActionResult();
    }

    private static ShelfDocumentResponse ToDocumentResponse(LibraryDocument d) => new(
        d.Id, d.Title, d.Kind.ToString(), d.FileName, d.ContentType, d.SizeBytes, d.PageCount);
}
