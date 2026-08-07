using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// The convergence gauge — a world's hidden material ranked by how ready it is to be revealed.
/// GM-only and read-only: it changes nothing and reveals nothing, and the reveal it points at
/// still goes through the reveal endpoint with the GM confirming.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/convergence")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class ConvergenceController : ControllerBase
{
    private readonly IConvergenceGaugeService _gaugeService;

    public ConvergenceController(IConvergenceGaugeService gaugeService)
    {
        _gaugeService = gaugeService;
    }

    /// <summary>Ranks the world's hidden material. GM-only.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _gaugeService.GetGaugeAsync(worldId, user.Id, member.Role, ct);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : result.Error!.ToActionResult();
    }

    private static ConvergenceResponse ToResponse(ConvergenceGauge gauge) => new(
        gauge.WorldId,
        gauge.GeneratedAt,
        gauge.AssessmentId,
        gauge.TotalCandidates,
        gauge.Candidates.Select(ToResponse).ToList());

    private static ConvergenceCandidateResponse ToResponse(ConvergenceCandidate candidate) => new(
        candidate.Kind.ToString(),
        candidate.Id,
        candidate.AnchorArtifactId,
        candidate.AnchorName,
        candidate.Description,
        candidate.CreatedAt,
        candidate.MissingArtifactIds,
        new ConvergenceComponentsResponse(
            candidate.Components.DaysHidden,
            candidate.Components.PartyVisibleFactsOnAnchor,
            candidate.Components.MissingArtifactCount,
            candidate.Components.IsSelfContained,
            candidate.Components.StorylineStatus?.ToString(),
            candidate.Components.ContradictionSeverity?.ToString(),
            candidate.Components.ContradictionAssessed,
            candidate.Components.Dormancy,
            candidate.Components.AnchorFamiliarity,
            candidate.Components.SelfContainment,
            candidate.Components.StorylineState,
            candidate.Components.ContradictionPressure),
        candidate.Score);
}
