using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/sources")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class SourcesController : ControllerBase
{
    private readonly ISourceService _sourceService;
    private readonly ILearnedDigestService _learnedService;

    public SourcesController(ISourceService sourceService, ILearnedDigestService learnedService)
    {
        _sourceService = sourceService;
        _learnedService = learnedService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid worldId,
        [FromBody] CreateSourceRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!EnumParsing.TryParseDefined<SourceType>(request.Type, out var sourceType))
        {
            return BadRequest(new ErrorResponse("invalid_source_type", $"'{request.Type}' is not a valid source type."));
        }

        if (!EnumParsing.TryParseDefined<VisibilityScope>(request.Visibility, out var visibility))
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
            CampaignId: request.CampaignId,
            ExtractionEnabled: request.ExtractionEnabled);

        var result = await _sourceService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
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

        var result = await _sourceService.ListSummariesByWorldAsync(
            worldId, user.Id, member.Role, ct, campaignFilter, unassignedOnly);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var response = result.Value!.Select(ToSourceListItemResponse).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Activity counts for nav badges: in-flight/failed sources + pending proposals.
    ///
    /// Two aggregate queries. This is the most frequently hit endpoint in the system — polled
    /// from every open tab for the life of the circuit — and it used to answer with the world's
    /// entire source table loaded twice (transcripts included), plus the full review queue with
    /// its proposals, batches and artifacts, all discarded to produce six integers.
    /// </summary>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _sourceService.GetActivityAsync(worldId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var activity = result.Value!;

        // Composed here rather than inside SourceService: the badge counts belong together on
        // one poll, but "how many disclosures has this reader not seen" is not a fact about
        // sources, and SourceService has no business knowing about a member's marker.
        var unseen = await _learnedService.CountUnseenAsync(worldId, user.Id, member.Role, ct);

        return Ok(new SourceActivityResponse(
            Ready: activity.Ready,
            Queued: activity.Queued,
            Processing: activity.Processing,
            Failed: activity.Failed,
            PendingProposals: activity.PendingProposals,
            PendingProposalsCapped: activity.PendingProposalsCapped,
            UnseenDisclosures: unseen.IsSuccess ? unseen.Value : 0));
    }

    [HttpGet("{sourceId:guid}")]
    public async Task<IActionResult> GetById(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _sourceService.GetByIdAsync(sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
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
            if (!EnumParsing.TryParseDefined<SourceType>(request.Type, out var parsedType))
            {
                return BadRequest(new ErrorResponse("invalid_source_type", $"'{request.Type}' is not a valid source type."));
            }
            sourceType = parsedType;
        }

        VisibilityScope? visibility = null;
        if (request.Visibility is not null)
        {
            if (!EnumParsing.TryParseDefined<VisibilityScope>(request.Visibility, out var parsedVisibility))
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
            ClearBody: request.ClearBody,
            Uri: request.Uri,
            ClearUri: request.ClearUri,
            OccurredAt: request.OccurredAt,
            ClearOccurredAt: request.ClearOccurredAt,
            Type: sourceType,
            Visibility: visibility,
            CampaignId: request.CampaignId,
            ClearCampaign: request.ClearCampaign,
            ExtractionEnabled: request.ExtractionEnabled);

        var result = await _sourceService.UpdateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
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
            return result.Error!.ToActionResult();
        }

        return NoContent();
    }

    /// <summary>The source's map image and its pins, filtered to what the caller may see.</summary>
    [HttpGet("{sourceId:guid}/map")]
    public async Task<IActionResult> GetMap(
        Guid worldId,
        Guid sourceId,
        [FromServices] IMapViewService mapViewService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await mapViewService.GetMapAsync(sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var map = result.Value!;
        return Ok(new MapViewResponse(
            ToAttachmentResponse(map.Attachment),
            map.ImageUrl,
            map.Placemarks
                .Select(p => new MapPlacemarkResponse(p.Id, p.ArtifactId, p.ArtifactName, p.X, p.Y, p.Label, p.Confidence))
                .ToList()));
    }

    /// <summary>Pins an existing Location artifact on this source's map, at its centre.
    /// Source creator or GM only; 409 when that location is already pinned here.</summary>
    [HttpPost("{sourceId:guid}/map/placemarks")]
    public async Task<IActionResult> CreatePlacemark(
        Guid worldId,
        Guid sourceId,
        [FromBody] CreatePlacemarkRequest request,
        [FromServices] IMapViewService mapViewService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await mapViewService.CreatePlacemarkAsync(
            sourceId, worldId, request.ArtifactId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var pin = result.Value!;
        return Ok(new MapPlacemarkResponse(pin.Id, pin.ArtifactId, pin.ArtifactName, pin.X, pin.Y, pin.Label, pin.Confidence));
    }

    /// <summary>Moves a map pin to a new normalized position. Source creator or GM only.</summary>
    [HttpPatch("{sourceId:guid}/map/placemarks/{placemarkId:guid}")]
    public async Task<IActionResult> MovePlacemark(
        Guid worldId,
        Guid sourceId,
        Guid placemarkId,
        [FromBody] MovePlacemarkRequest request,
        [FromServices] IMapViewService mapViewService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await mapViewService.MovePlacemarkAsync(
            sourceId, worldId, placemarkId, request.X, request.Y, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var pin = result.Value!;
        return Ok(new MapPlacemarkResponse(pin.Id, pin.ArtifactId, pin.ArtifactName, pin.X, pin.Y, pin.Label, pin.Confidence));
    }

    /// <summary>Removes a map pin; the pinned Location artifact is untouched. Source creator or GM only.</summary>
    [HttpDelete("{sourceId:guid}/map/placemarks/{placemarkId:guid}")]
    public async Task<IActionResult> RemovePlacemark(
        Guid worldId,
        Guid sourceId,
        Guid placemarkId,
        [FromServices] IMapViewService mapViewService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await mapViewService.RemovePlacemarkAsync(
            sourceId, worldId, placemarkId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return NoContent();
    }

    /// <summary>What this source's extraction contributed to the record, reader-visible only.</summary>
    [HttpGet("{sourceId:guid}/knowledge")]
    public async Task<IActionResult> GetKnowledge(
        Guid worldId,
        Guid sourceId,
        [FromServices] ISourceKnowledgeService knowledgeService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await knowledgeService.GetForSourceAsync(worldId, sourceId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var knowledge = result.Value!;
        return Ok(new SourceKnowledgeResponse(
            knowledge.Artifacts
                .Select(a => new SourceKnowledgeArtifactResponse(a.ArtifactId, a.Name, a.Type, a.Quote))
                .ToList(),
            knowledge.Facts
                .Select(f => new SourceKnowledgeFactResponse(
                    f.FactId, f.ArtifactId, f.ArtifactName, f.Predicate, f.Value, f.TruthState, f.Visibility, f.Quote))
                .ToList(),
            knowledge.Relationships
                .Select(r => new SourceKnowledgeRelationshipResponse(
                    r.RelationshipId, r.ArtifactAId, r.ArtifactAName, r.Type, r.ArtifactBId, r.ArtifactBName, r.Quote))
                .ToList()));
    }

    [HttpGet("{sourceId:guid}/reprocess-preview")]
    public async Task<IActionResult> GetReprocessPreview(
        Guid worldId,
        Guid sourceId,
        [FromServices] ISourceReprocessService reprocessService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await reprocessService.PreviewAsync(sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var preview = result.Value!;
        return Ok(new ReprocessPreviewResponse(
            preview.ArtifactNamesToDelete,
            preview.ArtifactNamesToKeep,
            preview.FactsToDelete,
            preview.RelationshipsToDelete,
            preview.PendingProposalsToDiscard,
            preview.MapPinsToDelete));
    }

    [HttpPost("{sourceId:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(
        Guid worldId,
        Guid sourceId,
        [FromBody] ReprocessSourceRequest request,
        [FromServices] ISourceReprocessService reprocessService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new ReprocessSourceCommand(
            SourceId: sourceId,
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            Title: request.Title,
            Body: request.Body,
            Uri: request.Uri,
            OccurredAt: request.OccurredAt,
            ClearOccurredAt: request.ClearOccurredAt);

        var result = await reprocessService.ReprocessAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToSourceResponse(result.Value!));
    }

    /// <summary>GM-only: reveals a GM-only source (and its attachments, e.g. a map image) to the
    /// party — the sanctioned exception to the post-extraction visibility lock.</summary>
    [HttpPost("{sourceId:guid}/reveal")]
    public async Task<IActionResult> Reveal(
        Guid worldId,
        Guid sourceId,
        [FromServices] IRevealService revealService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await revealService.RevealSourceAsync(worldId, sourceId, user.Id, member.Role, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        // The now-party-visible source, straight from the reveal — no refetch.
        return Ok(ToSourceResponse(result.Value!.Source));
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
            return result.Error!.ToActionResult();
        }

        var source = result.Value!;
        var response = ToSourceResponse(source);

        return Ok(response);
    }

    // ------------------------------------------------------------- Attachments --
    // Blob-backed files on a source (handwritten page images, ink documents), using the
    // same SAS handshake as the Library: request-upload → browser PUT → confirm.

    [HttpPost("{sourceId:guid}/attachments/request-upload")]
    public async Task<IActionResult> RequestAttachmentUpload(
        Guid worldId, Guid sourceId, [FromBody] RequestSourceAttachmentUploadRequest request,
        [FromServices] ISourceAttachmentService attachmentService, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!EnumParsing.TryParseDefined<SourceAttachmentKind>(request.Kind, out var kind))
        {
            return BadRequest(new ErrorResponse("invalid_kind", $"'{request.Kind}' is not a valid attachment kind."));
        }

        var command = new RequestSourceAttachmentUploadCommand(
            WorldId: worldId,
            SourceId: sourceId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            FileName: request.FileName,
            ContentType: request.ContentType,
            SizeBytes: request.SizeBytes,
            Kind: kind,
            Ord: request.Ord);

        var result = await attachmentService.RequestUploadAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var ticket = result.Value!;
        return Ok(new SourceAttachmentUploadResponse(ToAttachmentResponse(ticket.Attachment), ticket.UploadUrl));
    }

    [HttpPost("{sourceId:guid}/attachments/{attachmentId:guid}/confirm")]
    public async Task<IActionResult> ConfirmAttachmentUpload(
        Guid worldId, Guid sourceId, Guid attachmentId,
        [FromServices] ISourceAttachmentService attachmentService, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await attachmentService.ConfirmUploadAsync(attachmentId, sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToAttachmentResponse(result.Value!));
    }

    [HttpGet("{sourceId:guid}/attachments")]
    public async Task<IActionResult> ListAttachments(
        Guid worldId, Guid sourceId, [FromServices] ISourceAttachmentService attachmentService, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        // Read access rides on source read access: resolve the source through the same
        // visibility rules the detail endpoint uses before exposing attachment URLs.
        var sourceResult = await _sourceService.GetByIdAsync(sourceId, worldId, user.Id, member.Role, ct);
        if (!sourceResult.IsSuccess)
        {
            return sourceResult.Error!.ToActionResult();
        }

        var result = await attachmentService.ListAsync(sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var response = result.Value!
            .Select(x => ToAttachmentResponse(x.Attachment, x.Url))
            .ToList();

        return Ok(response);
    }

    [HttpDelete("{sourceId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(
        Guid worldId, Guid sourceId, Guid attachmentId,
        [FromServices] ISourceAttachmentService attachmentService, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await attachmentService.DeleteAsync(attachmentId, sourceId, worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return NoContent();
    }

    private static SourceAttachmentResponse ToAttachmentResponse(SourceAttachment attachment, string? url = null)
    {
        return new SourceAttachmentResponse(
            Id: attachment.Id,
            SourceId: attachment.SourceId,
            Kind: attachment.Kind.ToString(),
            FileName: attachment.FileName,
            ContentType: attachment.ContentType,
            SizeBytes: attachment.SizeBytes,
            Ord: attachment.Ord,
            Status: attachment.Status.ToString(),
            CreatedAt: attachment.CreatedAt,
            Url: url);
    }

    internal static SourceResponse ToSourceResponse(Source source)
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
            CampaignName: source.Campaign?.Name,
            ExtractionEnabled: source.ExtractionEnabled,
            DerivedText: source.DerivedText);
    }

    internal static SourceListItemResponse ToSourceListItemResponse(SourceListItem source)
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
            CampaignName: source.CampaignName);
    }
}
