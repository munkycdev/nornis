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
[Route("api/campaigns/{campaignId:guid}/sources")]
[ServiceFilter(typeof(CampaignMemberActionFilter))]
public class SourcesController : ControllerBase
{
    private readonly ISourceService _sourceService;

    public SourcesController(ISourceService sourceService)
    {
        _sourceService = sourceService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid campaignId,
        [FromBody] CreateSourceRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        if (!Enum.TryParse<SourceType>(request.Type, ignoreCase: true, out var sourceType))
        {
            return BadRequest(new ErrorResponse("invalid_source_type", $"'{request.Type}' is not a valid source type."));
        }

        if (!Enum.TryParse<VisibilityScope>(request.Visibility, ignoreCase: true, out var visibility))
        {
            return BadRequest(new ErrorResponse("invalid_visibility", $"'{request.Visibility}' is not a valid visibility scope."));
        }

        var command = new CreateSourceCommand(
            CampaignId: campaignId,
            Title: request.Title,
            Type: sourceType,
            Visibility: visibility,
            CreatingUserId: user.Id,
            CreatingUserRole: member.Role,
            Body: request.Body,
            Uri: request.Uri,
            OccurredAt: request.OccurredAt);

        var result = await _sourceService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var source = result.Value!;
        var response = ToSourceResponse(source);

        return CreatedAtAction(nameof(GetById), new { campaignId, sourceId = source.Id }, response);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid campaignId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        var result = await _sourceService.ListByCampaignAsync(campaignId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var sources = result.Value!;
        var response = sources.Select(ToSourceListItemResponse).ToList();

        return Ok(response);
    }

    [HttpGet("{sourceId:guid}")]
    public async Task<IActionResult> GetById(Guid campaignId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        var result = await _sourceService.GetByIdAsync(sourceId, campaignId, user.Id, member.Role, ct);

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
        Guid campaignId,
        Guid sourceId,
        [FromBody] UpdateSourceRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

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
            CampaignId: campaignId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Title: request.Title,
            Body: request.Body,
            Uri: request.Uri,
            OccurredAt: request.OccurredAt,
            Type: sourceType,
            Visibility: visibility);

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
    public async Task<IActionResult> Delete(Guid campaignId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        var result = await _sourceService.DeleteAsync(sourceId, campaignId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    [HttpPost("{sourceId:guid}/ready")]
    public async Task<IActionResult> MarkReady(Guid campaignId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        var command = new MarkSourceReadyCommand(
            SourceId: sourceId,
            CampaignId: campaignId,
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
            CampaignId: source.CampaignId,
            Type: source.Type.ToString(),
            Title: source.Title,
            Body: source.Body,
            Uri: source.Uri,
            OccurredAt: source.OccurredAt,
            CreatedAt: source.CreatedAt,
            CreatedByUserId: source.CreatedByUserId,
            Visibility: source.Visibility.ToString(),
            ProcessingStatus: source.ProcessingStatus.ToString());
    }

    private static SourceListItemResponse ToSourceListItemResponse(Source source)
    {
        return new SourceListItemResponse(
            Id: source.Id,
            CampaignId: source.CampaignId,
            Type: source.Type.ToString(),
            Title: source.Title,
            OccurredAt: source.OccurredAt,
            CreatedAt: source.CreatedAt,
            CreatedByUserId: source.CreatedByUserId,
            Visibility: source.Visibility.ToString(),
            ProcessingStatus: source.ProcessingStatus.ToString());
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
