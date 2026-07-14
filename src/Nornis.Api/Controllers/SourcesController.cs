using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/sources")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class SourcesController : ControllerBase
{
    private readonly ISourceService _sourceService;

    public SourcesController(ISourceService sourceService)
    {
        _sourceService = sourceService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid worldId,
        [FromBody] CreateSourceRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!Enum.TryParse<SourceType>(request.Type, ignoreCase: true, out var sourceType))
        {
            return BadRequest(new ErrorResponse("invalid_source_type", $"'{request.Type}' is not a valid source type."));
        }

        if (!Enum.TryParse<VisibilityScope>(request.Visibility, ignoreCase: true, out var visibility))
        {
            return BadRequest(new ErrorResponse("invalid_visibility", $"'{request.Visibility}' is not a valid visibility scope."));
        }

        var command = new CreateSourceCommand(
            WorldId: worldId,
            Title: request.Title,
            Type: sourceType,
            Visibility: visibility,
            CreatingUserId: user.Id,
            CreatingUserRole: member.Role,
            Body: request.Body,
            Uri: request.Uri,
            OccurredAt: request.OccurredAt,
            CampaignId: request.CampaignId);

        var result = await _sourceService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var source = result.Value!;
        var response = ToSourceResponse(source);

        return CreatedAtAction(nameof(GetById), new { worldId, sourceId = source.Id }, response);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid worldId, [FromQuery] string? campaignId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        // ?campaignId=<guid> filters to that campaign; ?campaignId=none to unassigned.
        Guid? campaignFilter = null;
        var unassignedOnly = false;
        if (!string.IsNullOrWhiteSpace(campaignId))
        {
            if (string.Equals(campaignId, "none", StringComparison.OrdinalIgnoreCase))
            {
                unassignedOnly = true;
            }
            else if (Guid.TryParse(campaignId, out var parsedCampaignId))
            {
                campaignFilter = parsedCampaignId;
            }
            else
            {
                return BadRequest(new ErrorResponse("invalid_campaign_filter", $"'{campaignId}' is not a valid campaign filter."));
            }
        }

        var result = await _sourceService.ListByWorldAsync(worldId, user.Id, member.Role, ct, campaignFilter, unassignedOnly);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var sources = result.Value!;
        var response = sources.Select(ToSourceListItemResponse).ToList();

        return Ok(response);
    }

    /// <summary>Activity counts for nav badges: in-flight/failed sources + pending proposals.</summary>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(
        Guid worldId,
        [FromServices] IReviewService reviewService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var sourcesResult = await _sourceService.ListByWorldAsync(worldId, user.Id, member.Role, ct);
        if (!sourcesResult.IsSuccess)
        {
            return MapError(sourcesResult.Error!);
        }

        var queueResult = await reviewService.ListReviewQueueAsync(
            new ReviewQueueQuery(worldId, user.Id, member.Role, null), ct);
        if (!queueResult.IsSuccess)
        {
            return MapError(queueResult.Error!);
        }

        var byStatus = sourcesResult.Value!
            .GroupBy(s => s.ProcessingStatus)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new SourceActivityResponse(
            Ready: byStatus.GetValueOrDefault(SourceProcessingStatus.Ready),
            Queued: byStatus.GetValueOrDefault(SourceProcessingStatus.Queued),
            Processing: byStatus.GetValueOrDefault(SourceProcessingStatus.Processing),
            Failed: byStatus.GetValueOrDefault(SourceProcessingStatus.Failed),
            PendingProposals: queueResult.Value!.Proposals.Count,
            PendingProposalsCapped: queueResult.Value.HasMore));
    }

    [HttpGet("{sourceId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _sourceService.GetByIdAsync(sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var source = result.Value!;
        var response = ToSourceResponse(source);

        return Ok(response);
    }

    [HttpPut("{sourceId:guid}")]
    public async Task<IActionResult> Update(
        Guid worldId,
        Guid sourceId,
        [FromBody] UpdateSourceRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        SourceType? sourceType = null;
        if (request.Type is not null)
        {
            if (!Enum.TryParse<SourceType>(request.Type, ignoreCase: true, out var parsedType))
            {
                return BadRequest(new ErrorResponse("invalid_source_type", $"'{request.Type}' is not a valid source type."));
            }
            sourceType = parsedType;
        }

        VisibilityScope? visibility = null;
        if (request.Visibility is not null)
        {
            if (!Enum.TryParse<VisibilityScope>(request.Visibility, ignoreCase: true, out var parsedVisibility))
            {
                return BadRequest(new ErrorResponse("invalid_visibility", $"'{request.Visibility}' is not a valid visibility scope."));
            }
            visibility = parsedVisibility;
        }

        var command = new UpdateSourceCommand(
            SourceId: sourceId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Title: request.Title,
            Body: request.Body,
            Uri: request.Uri,
            OccurredAt: request.OccurredAt,
            Type: sourceType,
            Visibility: visibility,
            CampaignId: request.CampaignId,
            ClearCampaign: request.ClearCampaign);

        var result = await _sourceService.UpdateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var source = result.Value!;
        var response = ToSourceResponse(source);

        return Ok(response);
    }

    [HttpDelete("{sourceId:guid}")]
    public async Task<IActionResult> Delete(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _sourceService.DeleteAsync(sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    [HttpPost("{sourceId:guid}/ready")]
    public async Task<IActionResult> MarkReady(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new MarkSourceReadyCommand(
            SourceId: sourceId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _sourceService.MarkReadyAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var source = result.Value!;
        var response = ToSourceResponse(source);

        return Ok(response);
    }

    private static SourceResponse ToSourceResponse(Source source)
    {
        return new SourceResponse(
            Id: source.Id,
            WorldId: source.WorldId,
            Type: source.Type.ToString(),
            Title: source.Title,
            Body: source.Body,
            Uri: source.Uri,
            OccurredAt: source.OccurredAt,
            CreatedAt: source.CreatedAt,
            CreatedByUserId: source.CreatedByUserId,
            Visibility: source.Visibility.ToString(),
            ProcessingStatus: source.ProcessingStatus.ToString(),
            CampaignId: source.CampaignId,
            CampaignName: source.Campaign?.Name);
    }

    private static SourceListItemResponse ToSourceListItemResponse(Source source)
    {
        return new SourceListItemResponse(
            Id: source.Id,
            WorldId: source.WorldId,
            Type: source.Type.ToString(),
            Title: source.Title,
            OccurredAt: source.OccurredAt,
            CreatedAt: source.CreatedAt,
            CreatedByUserId: source.CreatedByUserId,
            Visibility: source.Visibility.ToString(),
            ProcessingStatus: source.ProcessingStatus.ToString(),
            CampaignId: source.CampaignId,
            CampaignName: source.Campaign?.Name);
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
