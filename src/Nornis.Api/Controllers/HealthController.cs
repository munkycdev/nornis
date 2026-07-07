using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// Continuity health — a heuristic quality score for the campaign's recorded memory.
/// </summary>
[ApiController]
[Route("api/campaigns/{campaignId:guid}/health")]
[ServiceFilter(typeof(CampaignMemberActionFilter))]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid campaignId, CancellationToken ct)
    {
        var result = await _healthService.GetHealthAsync(campaignId, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    private static CampaignHealthResponse ToResponse(CampaignHealth h) =>
        new(h.HasData, h.OverallScore, h.Label, h.Consistency, h.Completeness,
            h.Groundedness, h.Recency, h.ArtifactCount, h.StatementCount);

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            403 => StatusCode(403, new ErrorResponse(error.Code, error.Message)),
            404 => NotFound(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(error.StatusCode, new ErrorResponse(error.Code, error.Message))
        };
    }
}
