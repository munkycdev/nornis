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
/// Continuity health — a heuristic quality score for the world's recorded memory, plus an
/// on-demand AI assessment that names specific continuity risks with artifact links.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/health")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class HealthController : ControllerBase
{
    private readonly IContinuityAuditService _auditService;
    private readonly IHealthService _healthService;

    public HealthController(IContinuityAuditService auditService, IHealthService healthService)
    {
        _auditService = auditService;
        _healthService = healthService;
    }

    /// <summary>The heuristic score with its component breakdown. GM-only.</summary>
    [HttpGet]
    public async Task<IActionResult> GetHeuristic(Guid worldId, CancellationToken ct)
    {
        if (RequireGm() is { } forbidden)
        {
            return forbidden;
        }

        var result = await _healthService.GetHealthAsync(worldId, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var h = result.Value!;
        return Ok(new WorldHealthResponse(
            h.HasData, h.OverallScore, h.Label,
            h.Consistency, h.Completeness, h.Groundedness, h.Recency,
            h.ArtifactCount, h.StatementCount));
    }

    /// <summary>Runs a fresh AI continuity assessment. GM-only; takes ~10-30s.</summary>
    [HttpPost("assess")]
    public async Task<IActionResult> Assess(Guid worldId, CancellationToken ct)
    {
        if (RequireGm() is { } forbidden)
        {
            return forbidden;
        }

        var user = HttpContext.GetNornisUser();
        var result = await _auditService.RunAssessmentAsync(worldId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    /// <summary>Returns the latest AI assessment with findings and the current effective score. GM-only.</summary>
    [HttpGet("assessment")]
    public async Task<IActionResult> GetAssessment(Guid worldId, CancellationToken ct)
    {
        if (RequireGm() is { } forbidden)
        {
            return forbidden;
        }

        var result = await _auditService.GetLatestAsync(worldId, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    /// <summary>Dismisses an Open finding (Open -> Dismissed). GM-only.</summary>
    [HttpPost("findings/{findingId:guid}/dismiss")]
    public async Task<IActionResult> DismissFinding(Guid worldId, Guid findingId, CancellationToken ct)
    {
        if (RequireGm() is { } forbidden)
        {
            return forbidden;
        }

        var result = await _auditService.DismissFindingAsync(worldId, findingId, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToResponse(result.Value!));
    }

    private IActionResult? RequireGm()
    {
        var member = HttpContext.GetWorldMember();
        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can run continuity assessments."));
        }
        return null;
    }

    private static ContinuityAssessmentResponse ToResponse(ContinuityAssessment a) =>
        new(a.HasData, a.AssessmentId, a.CreatedAt, a.Model, a.Score, a.EffectiveScore, a.HeuristicScore,
            a.Findings.Select(ToResponse).ToList());

    private static ContinuityFindingResponse ToResponse(ContinuityFindingView f) =>
        new(f.Id, f.Category, f.Severity, f.Summary, f.SuggestedAction, f.Evidence, f.ArtifactId, f.Status);

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
