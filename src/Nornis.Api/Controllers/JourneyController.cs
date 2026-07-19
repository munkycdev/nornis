using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// The Journey view: a world's map with the party's trail across the sessions that visited its
/// pinned locations. A read-only projection — it writes nothing. Any world member may read it;
/// the response is shaped by the caller's role, so a player and a GM get different journeys from
/// the same request.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/journey")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class JourneyController : ControllerBase
{
    private readonly IJourneyMapService _journeyService;

    public JourneyController(IJourneyMapService journeyService)
    {
        _journeyService = journeyService;
    }

    /// <summary>
    /// The journey over one map. Omit <paramref name="mapSourceId"/> to auto-pick the world's map
    /// with the most caller-visible pins; supply it to chart a specific map. 404 <c>no_map</c>
    /// when there is no caller-visible map with pins.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetJourney(Guid worldId, [FromQuery] Guid? mapSourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _journeyService.GetJourneyAsync(worldId, mapSourceId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    internal static JourneyResponse ToResponse(JourneyMap journey) =>
        new(
            journey.MapAttachmentId,
            journey.ImageUrl,
            journey.Locations
                .Select(l => new JourneyLocationResponse(l.ArtifactId, l.Name, l.X, l.Y, l.Label))
                .ToList(),
            journey.Stops
                .Select(s => new JourneyStopResponse(
                    s.SourceId, s.Title, s.OccurredAt, s.VisitedLocationIds,
                    s.Highlights
                        .Select(h => new JourneyHighlightResponse(h.ArtifactId, h.Name, h.Type, h.FirstSeen))
                        .ToList()))
                .ToList(),
            journey.UndatedSessionCount);

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            403 => StatusCode(403, new ErrorResponse(error.Code, error.Message)),
            404 => NotFound(new ErrorResponse(error.Code, error.Message)),
            409 => Conflict(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message))
        };
    }
}
