using Microsoft.AspNetCore.Mvc;
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
[Route("api/worlds/{worldId:guid}/artifacts")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class ArtifactsController : ControllerBase
{
    private readonly IArtifactService _artifactService;

    public ArtifactsController(IArtifactService artifactService)
    {
        _artifactService = artifactService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        Guid worldId,
        [FromQuery] string? type,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        ArtifactType? typeFilter = null;
        if (type is not null)
        {
            if (!Enum.TryParse<ArtifactType>(type, ignoreCase: true, out var parsedType))
            {
                return BadRequest(new ErrorResponse("invalid_artifact_type", $"'{type}' is not a valid artifact type."));
            }
            typeFilter = parsedType;
        }

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
            Type: typeFilter,
            Status: statusFilter);

        var result = await _artifactService.ListAsync(query, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var response = result.Value!.Select(ToListItemResponse).ToList();

        return Ok(response);
    }

    [HttpGet("{artifactId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid artifactId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _artifactService.GetDetailAsync(artifactId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToDetailResponse(result.Value!));
    }

    internal static ArtifactListItemResponse ToListItemResponse(Artifact artifact)
    {
        return new ArtifactListItemResponse(
            Id: artifact.Id,
            WorldId: artifact.WorldId,
            Type: artifact.Type.ToString(),
            Name: artifact.Name,
            Summary: artifact.Summary,
            Status: artifact.Status.ToString(),
            Visibility: artifact.Visibility.ToString(),
            Confidence: artifact.Confidence,
            CreatedAt: artifact.CreatedAt,
            UpdatedAt: artifact.UpdatedAt);
    }

    private static ArtifactDetailResponse ToDetailResponse(ArtifactDetail detail)
    {
        var artifact = detail.Artifact;

        return new ArtifactDetailResponse(
            Id: artifact.Id,
            WorldId: artifact.WorldId,
            Type: artifact.Type.ToString(),
            Name: artifact.Name,
            Summary: artifact.Summary,
            Status: artifact.Status.ToString(),
            Visibility: artifact.Visibility.ToString(),
            Confidence: artifact.Confidence,
            CreatedAt: artifact.CreatedAt,
            UpdatedAt: artifact.UpdatedAt,
            Facts: detail.Facts.Select(ToFactResponse).ToList(),
            Relationships: detail.Relationships.Select(ToRelationshipResponse).ToList(),
            ConnectedArtifacts: detail.ConnectedArtifacts.Select(ToConnectedResponse).ToList(),
            SourceReferences: detail.SourceReferences.Select(ToSourceReferenceResponse).ToList());
    }

    private static ArtifactFactResponse ToFactResponse(ArtifactFact fact)
    {
        return new ArtifactFactResponse(
            Id: fact.Id,
            ArtifactId: fact.ArtifactId,
            Predicate: fact.Predicate,
            Value: fact.Value,
            Confidence: fact.Confidence,
            TruthState: fact.TruthState.ToString(),
            Visibility: fact.Visibility.ToString(),
            CreatedAt: fact.CreatedAt,
            UpdatedAt: fact.UpdatedAt);
    }

    private static ArtifactRelationshipResponse ToRelationshipResponse(ArtifactRelationship relationship)
    {
        return new ArtifactRelationshipResponse(
            Id: relationship.Id,
            ArtifactAId: relationship.ArtifactAId,
            ArtifactBId: relationship.ArtifactBId,
            Type: relationship.Type,
            Description: relationship.Description,
            Confidence: relationship.Confidence,
            TruthState: relationship.TruthState.ToString(),
            Visibility: relationship.Visibility.ToString());
    }

    private static ConnectedArtifactResponse ToConnectedResponse(Artifact artifact)
    {
        return new ConnectedArtifactResponse(
            Id: artifact.Id,
            Name: artifact.Name,
            Type: artifact.Type.ToString());
    }

    private static SourceReferenceResponse ToSourceReferenceResponse(SourceReference reference)
    {
        return new SourceReferenceResponse(
            Id: reference.Id,
            SourceId: reference.SourceId,
            TargetType: reference.TargetType.ToString(),
            TargetId: reference.TargetId,
            Quote: reference.Quote,
            Notes: reference.Notes,
            CreatedAt: reference.CreatedAt);
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
