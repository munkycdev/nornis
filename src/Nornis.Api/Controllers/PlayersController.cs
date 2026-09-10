using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// The people at the table. Every member reads the list; the GM adds, renames and removes
/// the ones who are not on Nornis; a member claims one as themselves, or the GM links one to
/// a member for them. A mutation answers with the player as the list would show it, resolved
/// through the same service, so the two cannot drift in what they call someone.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/players")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class PlayersController : ControllerBase
{
    private readonly IPlayerService _players;

    public PlayersController(IPlayerService players)
    {
        _players = players;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, CancellationToken ct)
    {
        var result = await _players.ListByWorldAsync(worldId, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(ToResponse).ToList())
            : result.Error!.ToActionResult();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Mine(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var result = await _players.GetMineAsync(worldId, user.Id, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid worldId, [FromBody] CreatePlayerRequest request, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _players.CreateAsync(worldId, request.Name, member.Role, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return CreatedAtAction(nameof(List), new { worldId }, await AsListedAsync(worldId, result.Value!.Id, ct));
    }

    [HttpPut("{playerId:guid}")]
    public async Task<IActionResult> Rename(Guid worldId, Guid playerId, [FromBody] RenamePlayerRequest request, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _players.RenameAsync(playerId, worldId, request.Name, member.Role, ct);
        return result.IsSuccess
            ? Ok(await AsListedAsync(worldId, result.Value!.Id, ct))
            : result.Error!.ToActionResult();
    }

    [HttpDelete("{playerId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid playerId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _players.DeleteAsync(playerId, worldId, member.Role, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    /// <summary>The caller takes this unlinked player as themselves. Answers with their own player.</summary>
    [HttpPost("{playerId:guid}/claim")]
    public async Task<IActionResult> Claim(Guid worldId, Guid playerId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();
        var result = await _players.ClaimAsync(playerId, worldId, user.Id, member.Role, ct);
        return result.IsSuccess
            ? Ok(await AsListedAsync(worldId, result.Value!.Id, ct))
            : result.Error!.ToActionResult();
    }

    /// <summary>A GM links this unlinked player to a member. Answers with the member's player.</summary>
    [HttpPut("{playerId:guid}/member")]
    public async Task<IActionResult> Link(Guid worldId, Guid playerId, [FromBody] LinkPlayerRequest request, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _players.LinkAsync(playerId, worldId, request.WorldMemberId, member.Role, ct);
        return result.IsSuccess
            ? Ok(await AsListedAsync(worldId, result.Value!.Id, ct))
            : result.Error!.ToActionResult();
    }

    private async Task<PlayerResponse?> AsListedAsync(Guid worldId, Guid playerId, CancellationToken ct)
    {
        var list = await _players.ListByWorldAsync(worldId, ct);
        var view = list.Value!.FirstOrDefault(p => p.Id == playerId);
        return view is null ? null : ToResponse(view);
    }

    internal static PlayerResponse ToResponse(PlayerView view) =>
        new(view.Id, view.WorldId, view.Name, view.WorldMemberId, view.Role?.ToString());
}
