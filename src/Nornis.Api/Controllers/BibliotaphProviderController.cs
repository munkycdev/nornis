using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Authentication;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Application.Bibliotaph;

namespace Nornis.Api.Controllers;

/// <summary>
/// What Nornis provides to Bibliotaph's Nornis storage provider (feature 31, Requirement 5).
/// Only Bibliotaph's own application, with scope <c>published:read</c>, reaches these, and only
/// these: the default policy refuses its tokens everywhere else. The caller is always the user
/// the token names; there is no world or file of anyone else's to ask for.
/// </summary>
[ApiController]
[Route("api/bibliotaph/v1")]
[Authorize(Policy = BibliotaphTokens.PolicyName)]
public class BibliotaphProviderController : ControllerBase
{
    private readonly IBibliotaphProviderService _provider;

    public BibliotaphProviderController(IBibliotaphProviderService provider)
    {
        _provider = provider;
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var user = HttpContext.GetNornisUser();
        return Ok(new BibliotaphMeResponse(user.Id, user.Username));
    }

    [HttpGet("worlds")]
    public async Task<IActionResult> Worlds([FromQuery] string? cursor, CancellationToken ct)
    {
        var result = await _provider.ListWorldsAsync(HttpContext.GetNornisUser().Id, cursor, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var page = result.Value!;
        return Ok(new BibliotaphProviderWorldsResponse(
            page.Items.Select(w => new BibliotaphProviderWorld(w.Id, w.Name, w.UpdatedAt)).ToList(),
            page.NextCursor));
    }

    [HttpGet("worlds/{worldId:guid}/files")]
    public async Task<IActionResult> Files(Guid worldId, [FromQuery] string? cursor, CancellationToken ct)
    {
        var result = await _provider.ListFilesAsync(HttpContext.GetNornisUser().Id, worldId, cursor, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var page = result.Value!;
        return Ok(new BibliotaphProviderFilesResponse(
            page.Items.Select(d => new BibliotaphProviderFile(
                d.Id, d.FileName, d.ContentType, d.SizeBytes, d.UpdatedAt,
                d.UpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture))).ToList(),
            page.NextCursor));
    }

    [HttpGet("files/{fileId:guid}/link")]
    public async Task<IActionResult> Link(Guid fileId, CancellationToken ct)
    {
        var result = await _provider.GetLinkAsync(HttpContext.GetNornisUser().Id, fileId, ct);
        return result.IsSuccess
            ? Ok(new BibliotaphFileLinkResponse(result.Value!.Url, result.Value.ExpiresAt))
            : result.Error!.ToActionResult();
    }

    [HttpPost("resync")]
    public async Task<IActionResult> Resync(CancellationToken ct)
    {
        await _provider.ResyncAsync(HttpContext.GetNornisUser().Id, ct);
        return Accepted();
    }
}
