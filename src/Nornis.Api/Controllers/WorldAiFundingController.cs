using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Accounts;

namespace Nornis.Api.Controllers;

/// <summary>
/// How a world's AI is paid for, for its GMs (feature 39): the Web's "Trial: $1.40 left" line. Its
/// own small route rather than a field on the world, because the world list is what the Web loads
/// and a funding lookup per world there would be a query per row.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/ai-funding")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class WorldAiFundingController : ControllerBase
{
    private readonly IAiKeyService _keys;

    public WorldAiFundingController(IAiKeyService keys)
    {
        _keys = keys;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var result = await _keys.GetWorldFundingAsync(
            worldId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role, ct);

        return result.IsSuccess
            ? Ok(new WorldAiFundingResponse(result.Value!.Trial, result.Value.TrialRemainingUsd, result.Value.CreatorIsYou))
            : result.Error!.ToActionResult();
    }
}
