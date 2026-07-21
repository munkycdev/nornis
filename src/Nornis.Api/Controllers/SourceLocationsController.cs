using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// A session's location links: which pinned places it took place at. A person draws these on the
/// session page to correct what extraction could only guess; each is an ordinary SourceReference,
/// so a link also lights up the Journey trail. Any world member may read the links (shaped by their
/// visibility); only the source's creator or a GM may add or remove them.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/sources/{sourceId:guid}/locations")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class SourceLocationsController : ControllerBase
{
    private readonly ISourceLocationService _service;

    public SourceLocationsController(ISourceLocationService service)
    {
        _service = service;
    }

    /// <summary>The Location artifacts this session is linked to, visible to the caller, by name.</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _service.ListLocationsAsync(sourceId, worldId, user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    /// <summary>Links this session to a Location (idempotent). Returns the updated set.</summary>
    [HttpPost]
    public async Task<IActionResult> Link(
        Guid worldId, Guid sourceId, [FromBody] LinkLocationRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _service.LinkLocationAsync(sourceId, worldId, request.ArtifactId, user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : MapError(result.Error!);
    }

    /// <summary>Removes this session's link to a Location (any link — extractor- or user-authored).</summary>
    [HttpDelete("{artifactId:guid}")]
    public async Task<IActionResult> Unlink(Guid worldId, Guid sourceId, Guid artifactId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _service.UnlinkLocationAsync(sourceId, worldId, artifactId, user.Id, member.Role, ct);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    private static IReadOnlyList<LinkedLocationResponse> ToResponse(IReadOnlyList<LinkedLocation> locations) =>
        locations.Select(l => new LinkedLocationResponse(l.ArtifactId, l.Name, l.Summary)).ToList();

    private IActionResult MapError(AppError error) => error.StatusCode switch
    {
        400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
        403 => StatusCode(403, new ErrorResponse(error.Code, error.Message)),
        404 => NotFound(new ErrorResponse(error.Code, error.Message)),
        409 => Conflict(new ErrorResponse(error.Code, error.Message)),
        _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message))
    };
}
