using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Application.Errors;
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
    private readonly IJourneyMapService _journeyService;
    private readonly ILoremasterService _loremasterService;
    private readonly IAiBudgetGuard _budgetGuard;

    public PublicController(
        IWorldRepository worldRepository,
        IArtifactService artifactService,
        ISourceService sourceService,
        IJourneyMapService journeyService,
        ILoremasterService loremasterService,
        IAiBudgetGuard budgetGuard)
    {
        _worldRepository = worldRepository;
        _artifactService = artifactService;
        _sourceService = sourceService;
        _journeyService = journeyService;
        _loremasterService = loremasterService;
        _budgetGuard = budgetGuard;
    }

    [HttpGet("")]
    public async Task<IActionResult> GetWorld(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        return world is null
            ? PublicNotFound()
            : Ok(new PublicWorldResponse(
                world.PublicSlug!, world.Name, world.Description, world.GameSystem,
                AskEnabled: world.PublicAskMonthlyBudgetUsd is > 0m));
    }

    /// <summary>
    /// Anonymous "Ask the Loremaster", scoped to party-visible knowledge (Observer) and gated by
    /// the GM's monthly public spend cap. Single-shot (no conversation) and Library-free by design.
    /// A stricter per-IP limiter rides on top of the controller's <c>public</c> policy; the monthly
    /// cap is the hard money ceiling. Disabled worlds answer 404 like any unavailable resource.
    /// </summary>
    [HttpPost("ask")]
    [EnableRateLimiting("public-ask")]
    public async Task<IActionResult> Ask(string slug, [FromBody] PublicAskRequest request, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var gate = await _budgetGuard.CheckPublicAskAsync(world.Id, ct);
        if (gate is not null)
        {
            return MapAskError(gate);
        }

        var command = new AskLoremasterCommand(
            WorldId: world.Id,
            Question: request.Question,
            UserId: null,
            UserRole: PublicRole,
            ConversationContext: null,
            IncludeLibrary: false);

        var result = await _loremasterService.AskAsync(command, ct);
        return result.IsSuccess
            ? Ok(LoremasterController.ToAnswerResponse(result.Value!))
            : MapAskError(result.Error!);
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

    [HttpGet("artifacts/graph")]
    public async Task<IActionResult> GetArtifactGraph(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _artifactService.GetGraphAsync(world.Id, Guid.Empty, PublicRole, ct);
        return result.IsSuccess ? Ok(ArtifactsController.ToGraphResponse(result.Value!)) : PublicNotFound();
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

    /// <summary>
    /// The journey over the world's richest party-visible map. No map picker on the public
    /// side — anonymous readers get the auto-picked map the members' view defaults to.
    /// </summary>
    [HttpGet("journey")]
    public async Task<IActionResult> GetJourney(string slug, CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await _journeyService.GetJourneyAsync(world.Id, null, AnonymousUserId, PublicRole, ct);
        if (result.IsSuccess)
        {
            return Ok(JourneyController.ToResponse(result.Value!));
        }

        // "No party-visible map with pins" is the page's empty state, not a miss, so it
        // keeps its own code. It is not an existence oracle either — the world endpoint
        // already answers 200-vs-404 for the same slug.
        return result.Error!.Code == "no_map"
            ? NotFound(new ErrorResponse(result.Error.Code, result.Error.Message))
            : PublicNotFound();
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
            ? Ok(result.Value!
                .Where(s => s.Type is SourceType.SessionNote or SourceType.ImportedNote)
                .Select(SourcesController.ToSourceListItemResponse)
                .ToList())
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

    /// <summary>What a public session contributed to the record — Observer-scoped like every read.</summary>
    [HttpGet("sources/{sourceId:guid}/knowledge")]
    public async Task<IActionResult> GetSourceKnowledge(
        string slug,
        Guid sourceId,
        [FromServices] ISourceKnowledgeService knowledgeService,
        CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await knowledgeService.GetForSourceAsync(world.Id, sourceId, AnonymousUserId, PublicRole, ct);
        if (!result.IsSuccess)
        {
            return PublicNotFound();
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

    /// <summary>The Location artifacts a public session is linked to.</summary>
    [HttpGet("sources/{sourceId:guid}/locations")]
    public async Task<IActionResult> GetSourceLocations(
        string slug,
        Guid sourceId,
        [FromServices] ISourceLocationService locationService,
        CancellationToken ct)
    {
        var world = await ResolveAsync(slug, ct);
        if (world is null)
        {
            return PublicNotFound();
        }

        var result = await locationService.ListLocationsAsync(sourceId, world.Id, AnonymousUserId, PublicRole, ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(l => new LinkedLocationResponse(l.ArtifactId, l.Name, l.Summary)).ToList())
            : PublicNotFound();
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

    // Ask failures surface their real code/message (budget spent, disabled, model unavailable)
    // rather than the generic public 404 — the asker needs to know why nothing came back.
    private IActionResult MapAskError(AppError error) => error.StatusCode switch
    {
        400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
        404 => NotFound(new ErrorResponse(error.Code, error.Message)),
        429 => StatusCode(429, new ErrorResponse(error.Code, error.Message)),
        503 => StatusCode(503, new ErrorResponse(error.Code, error.Message)),
        _ => StatusCode(500, new ErrorResponse("internal_error", "Something went wrong. Please try again.")),
    };
}
