using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// Canon is the truth-state view over accepted facts and relationships. Returns a flat,
/// visibility-scoped list of canon entries (newest first); the client groups them into
/// sections. Hidden entries are only returned to GMs.
/// </summary>
[ApiController]
[Route("api/campaigns/{campaignId:guid}/canon")]
[ServiceFilter(typeof(CampaignMemberActionFilter))]
public class CanonController : ControllerBase
{
    private readonly ICanonService _canonService;

    public CanonController(ICanonService canonService)
    {
        _canonService = canonService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid campaignId,
        [FromQuery] string? truthState,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        TruthState? truthStateFilter = null;
        if (truthState is not null)
        {
            if (!Enum.TryParse<TruthState>(truthState, ignoreCase: true, out var parsed))
            {
                return BadRequest(new ErrorResponse("invalid_truth_state", $"'{truthState}' is not a valid truth state."));
            }
            truthStateFilter = parsed;
        }

        var query = new CanonQuery(
            CampaignId: campaignId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            TruthState: truthStateFilter);

        var result = await _canonService.GetCanonAsync(query, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = result.Value!.Select(ToCanonEntryResponse).ToList();

        return Ok(response);
    }

    private static CanonEntryResponse ToCanonEntryResponse(CanonEntry entry)
    {
        return new CanonEntryResponse(
            Kind: entry.Kind.ToString(),
            Id: entry.Id,
            ArtifactId: entry.ArtifactId,
            ArtifactName: entry.ArtifactName,
            OtherArtifactId: entry.OtherArtifactId,
            OtherArtifactName: entry.OtherArtifactName,
            Label: entry.Label,
            Detail: entry.Detail,
            Confidence: entry.Confidence,
            TruthState: entry.TruthState.ToString(),
            Visibility: entry.Visibility.ToString(),
            UpdatedAt: entry.UpdatedAt);
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
