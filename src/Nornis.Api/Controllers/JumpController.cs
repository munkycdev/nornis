using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// The quick switcher's endpoint. One query, five kinds, each already filtered to the reader
/// by the service that owns it — see <see cref="JumpService"/>.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/jump")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class JumpController : ControllerBase
{
    private readonly IJumpService _jumpService;

    public JumpController(IJumpService jumpService)
    {
        _jumpService = jumpService;
    }

    [HttpGet]
    public async Task<IActionResult> Jump(Guid worldId, [FromQuery] string? q, [FromQuery] int? perKind, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _jumpService.JumpAsync(
            new JumpQuery(worldId, user.Id, member.Role, q ?? string.Empty, perKind ?? 8), ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(new JumpResponse(result.Value!.Groups.Select(g => new JumpGroupResponse(
            Kind: g.Kind.ToString(),
            Items: g.Items.Select(i => new JumpItemResponse(i.Id, i.Name, i.Detail)).ToList(),
            TotalCount: g.TotalCount)).ToList()));
    }
}
