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
    private readonly IContinuityFixService _fixService;

    public HealthController(IContinuityAuditService auditService, IContinuityFixService fixService)
    {
        _auditService = auditService;
        _fixService = fixService;
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

    /// <summary>
    /// Drafts review-queue proposals that address an open finding across every evidence leg it
    /// cites. GM-only; nothing changes canon until the proposals are accepted in the queue.
    /// </summary>
    [HttpPost("findings/{findingId:guid}/draft-fix")]
    public async Task<IActionResult> DraftFix(Guid worldId, Guid findingId, CancellationToken ct)
    {
        if (RequireGm() is { } forbidden)
        {
            return forbidden;
        }

        var user = HttpContext.GetNornisUser();
        var result = await _fixService.DraftFixAsync(worldId, findingId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var draft = result.Value!;
        return Ok(new DraftFixResponse(draft.BatchId, draft.SourceId, draft.ProposalCount));
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
        new(f.Id, f.Category, f.Severity, f.Summary, f.SuggestedAction, f.Evidence,
            f.EvidenceItems.Select(ToResponse).ToList(), f.ArtifactId, f.Status, f.IsStale);

    private static ContinuityEvidenceItemResponse ToResponse(ContinuityEvidenceItemView e) =>
        new(e.RefId, e.Kind, e.Label, e.ArtifactId, e.ChangedSinceAudit, e.Missing);

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
