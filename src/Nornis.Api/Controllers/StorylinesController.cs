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
/// Storylines are artifacts with <see cref="ArtifactType.Storyline"/>. This is a dedicated
/// read view over the same artifact data, matching the Storylines navigation item.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/storylines")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class StorylinesController : ControllerBase
{
    private readonly IArtifactService _artifactService;
    private readonly IStorylineRetrospectiveService _retrospectiveService;
    private readonly IRelationshipBackfillQueueService _backfillQueueService;

    public StorylinesController(
        IArtifactService artifactService,
        IStorylineRetrospectiveService retrospectiveService,
        IRelationshipBackfillQueueService backfillQueueService)
    {
        _artifactService = artifactService;
        _retrospectiveService = retrospectiveService;
        _backfillQueueService = backfillQueueService;
    }

    /// <summary>
    /// GM-only: assess every Active storyline against the record and propose closures
    /// as review proposals.
    /// </summary>
    [HttpPost("retrospective")]
    public async Task<IActionResult> RunRetrospective(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _retrospectiveService.RunAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var value = result.Value!;
        return Ok(new RetrospectiveResponse(value.AssessedCount, value.ProposedCount, value.ReviewBatchId));
    }

    /// <summary>
    /// GM-only: queue the relationship backfill sweep — one worker message per processed
    /// source not yet swept, each producing Advances/PartOf link proposals for review.
    /// Safe to re-run; already-swept sources are skipped.
    /// </summary>
    [HttpPost("backfill-relationships")]
    public async Task<IActionResult> QueueRelationshipBackfill(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();

        var result = await _backfillQueueService.QueueBackfillAsync(worldId, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var value = result.Value!;
        return Ok(new BackfillQueueResponse(value.QueuedCount, value.AlreadySweptCount, value.TotalEligible));
    }

    /// <summary>
    /// The storyline timeline: lanes of session-dated developments per storyline, the
    /// sessions that produced them, and explicit storyline-to-storyline links.
    /// </summary>
    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _artifactService.GetStorylineTimelineAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToTimelineResponse(result.Value!));
    }

    /// <summary>Shared with the public read-only endpoints.</summary>
    internal static StorylineTimelineResponse ToTimelineResponse(Application.Models.StorylineTimeline timeline) =>
        new(
            timeline.Sessions.Select(s => new TimelineSessionResponse(s.SourceId, s.Title, s.OccurredAt, s.StorylineCount)).ToList(),
            timeline.Lanes.Select(l => new TimelineLaneResponse(
                l.StorylineId, l.Name, l.Status,
                l.Points.Select(p => new TimelinePointResponse(
                    p.SourceId, p.OccurredAt,
                    p.Developments.Select(d => new TimelineDevelopmentResponse(d.Kind, d.Text, d.Quote, d.IsOpenQuestion)).ToList())).ToList(),
                l.ParentStorylineId, l.CampaignName)).ToList(),
            timeline.Links.Select(x => new TimelineLinkResponse(x.FromStorylineId, x.ToStorylineId, x.Type)).ToList());

    [HttpGet]
    public async Task<IActionResult> List(
        Guid worldId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        ArtifactStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ArtifactStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                return BadRequest(new ErrorResponse("invalid_artifact_status", $"'{status}' is not a valid artifact status."));
            }
            statusFilter = parsedStatus;
        }

        var query = new ArtifactListQuery(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Type: ArtifactType.Storyline,
            Status: statusFilter);

        var result = await _artifactService.ListAsync(query, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = result.Value!.Select(ArtifactsController.ToListItemResponse).ToList();

        return Ok(response);
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
