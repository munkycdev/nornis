using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Services;
using Nornis.Domain.Entities;

namespace Nornis.Api.Controllers;

/// <summary>
/// GM management of a world's shelf links: mint one for a player who is not on Nornis, see the
/// standing ones, take one back. World-scoped and GM-only, enforced in the service. Opening a
/// link lives on the anonymous <see cref="ShelfController"/>, because the holder is by
/// definition not a member.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/shelf-links")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class ShelfLinksController : ControllerBase
{
    private readonly IShelfLinkService _shelfLinks;

    public ShelfLinksController(IShelfLinkService shelfLinks)
    {
        _shelfLinks = shelfLinks;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _shelfLinks.ListActiveAsync(worldId, member.Role, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToList())
            : result.Error!.ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid worldId, [FromBody] CreateShelfLinkRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();
        var result = await _shelfLinks.CreateAsync(worldId, request.PlayerId, user.Id, member.Role, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), new { worldId }, ToResponse(result.Value!))
            : result.Error!.ToActionResult();
    }

    [HttpDelete("{linkId:guid}")]
    public async Task<IActionResult> Revoke(Guid worldId, Guid linkId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _shelfLinks.RevokeAsync(worldId, linkId, member.Role, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    internal static ShelfLinkResponse ToResponse(ShelfLink link) =>
        new(link.Id, link.PlayerId, link.Code, link.CreatedAt, link.LastUsedAt);
}
