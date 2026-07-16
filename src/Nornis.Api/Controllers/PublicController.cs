using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nornis.Api.Contracts.Responses;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Controllers;

/// <summary>
/// Anonymous read-only access to a world's party-visible knowledge, gated by the
/// GM-defined public slug. Every read runs as <see cref="WorldRole.Observer"/> with a
/// sentinel user id — Observer scoping is exactly "PartyVisible, no Hidden truths", so
/// the existing services enforce the whole policy. Unknown slugs and disabled worlds
/// return identical 404s (no existence oracle). No Library, no Ask, no mutations.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public")]
[Route("api/public/worlds/{slug}")]
public class PublicController : ControllerBase
{
    private static readonly Guid AnonymousUserId = Guid.Empty;
    private const WorldRole PublicRole = WorldRole.Observer;

    private readonly IWorldRepository _worldRepository;
    private readonly IArtifactService _artifactService;
    private readonly ISourceService _sourceService;

    public PublicController(
        IWorldRepository worldRepository,
        IArtifactService artifactService,
        ISourceService sourceService)
    {
        _worldRepository = worldRepository;
        _artifactService = artifactService;
        _sourceService = sourceService;
    }

    [HttpGet("")]
    public async Task<IActionResult> GetWorld(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        return world is null
            ? PublicNotFound()
            : Ok(new PublicWorldResponse(world.PublicSlug!, world.Name, world.Description, world.GameSystem));
    }

    [HttpGet("artifacts")]
    public async Task<IActionResult> ListArtifacts(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _artifactService.ListAsync(
            new ArtifactListQuery(world.Id, AnonymousUserId, PublicRole, null, null), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(ArtifactsController.ToListItemResponse).ToList())
            : PublicNotFound();
    }

    [HttpGet("artifacts/{artifactId:guid}")]
    public async Task<IActionResult> GetArtifact(string slug, Guid artifactId, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _artifactService.GetDetailAsync(artifactId, world.Id, AnonymousUserId, PublicRole, ct);
        return result.IsSuccess ? Ok(ArtifactsController.ToDetailResponse(result.Value!)) : PublicNotFound();
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _artifactService.GetStorylineTimelineAsync(world.Id, AnonymousUserId, PublicRole, ct);
        return result.IsSuccess ? Ok(StorylinesController.ToTimelineResponse(result.Value!)) : PublicNotFound();
    }

    [HttpGet("sources")]
    public async Task<IActionResult> ListSources(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _sourceService.ListByWorldAsync(world.Id, AnonymousUserId, PublicRole, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(SourcesController.ToSourceListItemResponse).ToList())
            : PublicNotFound();
    }

    [HttpGet("sources/{sourceId:guid}")]
    public async Task<IActionResult> GetSource(string slug, Guid sourceId, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _sourceService.GetByIdAsync(sourceId, world.Id, AnonymousUserId, PublicRole, ct);
        return result.IsSuccess ? Ok(SourcesController.ToSourceResponse(result.Value!)) : PublicNotFound();
    }

    private async Task<World?> ResolveAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 60)
        {
            return null;
        }

        var world = await _worldRepository.GetBySlugAsync(slug, ct);
        return world is { PublicAccessEnabled: true } ? world : null;
    }

    private NotFoundObjectResult PublicNotFound() =>
        NotFound(new ErrorResponse("not_found", "This world is not publicly accessible."));
}
