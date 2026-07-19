using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/reveal")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class RevealController : ControllerBase
{
    private readonly IRevealService _revealService;

    public RevealController(IRevealService revealService)
    {
        _revealService = revealService;
    }

    /// <summary>
    /// GM-only: promotes a curated set of GM-only knowledge to the party. Returns 422 with the
    /// missing artifacts when the set is not reference-closed (nothing is changed in that case).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Reveal(
        Guid worldId,
        [FromBody] RevealRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var corrections = new List<FactCorrection>();
        foreach (var correction in request.Corrections ?? [])
        {
            if (!Enum.TryParse<TruthState>(correction.TruthState, ignoreCase: true, out var truthState))
            {
                return BadRequest(new ErrorResponse("invalid_truth_state",
                    $"'{correction.TruthState}' is not a valid truth state."));
            }
            corrections.Add(new FactCorrection(correction.FactId, truthState));
        }

        var command = new RevealCommand(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            ArtifactIds: request.ArtifactIds ?? [],
            FactIds: request.FactIds ?? [],
            RelationshipIds: request.RelationshipIds ?? [],
            Corrections: corrections,
            Note: request.Note);

        var result = await _revealService.RevealAsync(command, ct);
        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var reveal = result.Value!;
        if (!reveal.IsClosed)
        {
            return UnprocessableEntity(new RevealNotClosedResponse(
                "reveal_not_closed",
                "The reveal is missing dependencies — include the listed artifacts and resubmit.",
                reveal.MissingArtifactIds));
        }

        return Ok(new RevealResponse(
            reveal.BatchId,
            reveal.RevealedArtifacts,
            reveal.RevealedFacts,
            reveal.RevealedRelationships,
            reveal.Corrections));
    }

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
