using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/facts")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class FactsController : ControllerBase
{
    private readonly IFactRemovalService _factRemovalService;

    public FactsController(IFactRemovalService factRemovalService)
    {
        _factRemovalService = factRemovalService;
    }

    /// <summary>GM-only: removes an incorrect fact from canon, recording the reason as a GM note.</summary>
    [HttpPost("{factId:guid}/removal")]
    public async Task<IActionResult> Remove(
        Guid worldId,
        Guid factId,
        [FromBody] RemoveFactRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new RemoveFactCommand(worldId, factId, request.Note, user.Id, member.Role);
        var result = await _factRemovalService.RemoveAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return NoContent();
    }
}
