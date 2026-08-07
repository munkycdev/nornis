using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// What the party has been told. The one world-scoped surface with no role gate beyond
/// membership — its whole audience is the people without privileges, and it reads at the
/// caller's own visibility, so a Player and an Observer see the same thing and neither sees a
/// trace of what has not been disclosed.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/learned")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class LearnedController : ControllerBase
{
    private readonly ILearnedDigestService _learnedService;

    public LearnedController(ILearnedDigestService learnedService)
    {
        _learnedService = learnedService;
    }

    /// <summary>Reveals this member has not yet seen, newest first. Reading is not seeing.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _learnedService.GetAsync(worldId, user.Id, member.Role, ct);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : result.Error!.ToActionResult();
    }

    /// <summary>
    /// Advances this member's marker. Idempotent: an older or repeated point is accepted and
    /// changes nothing, because a second tab is not a conflict.
    /// </summary>
    [HttpPost("seen")]
    public async Task<IActionResult> MarkSeen(
        Guid worldId, [FromBody] MarkLearnedSeenRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await _learnedService.MarkSeenAsync(worldId, user.Id, request.SeenThrough, ct);

        return result.IsSuccess
            ? Ok(new { seenThrough = result.Value })
            : result.Error!.ToActionResult();
    }

    private static LearnedResponse ToResponse(LearnedDigest digest) => new(
        digest.WorldId,
        digest.GeneratedAt,
        digest.SeenThrough,
        digest.HasMore,
        digest.Entries.Select(ToResponse).ToList());

    private static LearnedEntryResponse ToResponse(LearnedEntry entry) => new(
        entry.SourceId,
        entry.OccurredAt,
        entry.GmNote,
        entry.Elements
            .Select(e => new LearnedElementResponse(e.Id, e.Kind, e.Name, e.Detail))
            .ToList());
}
