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
[Route("api/worlds")]
public class WorldsController : ControllerBase
{
    private readonly IWorldService _worldService;

    public WorldsController(IWorldService worldService)
    {
        _worldService = worldService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorldRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var command = new CreateWorldCommand(
            Name: request.Name,
            Description: request.Description,
            GameSystem: request.GameSystem,
            CreatingUserId: user.Id);

        var result = await _worldService.CreateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var world = result.Value!;
        var response = ToWorldResponse(world, WorldRole.GM);

        return CreatedAtAction(nameof(GetById), new { worldId = world.Id }, response);
    }

    // IDemoWorldService is action-injected like export/deletion: it depends on blob
    // storage, whose DI stub throws when unconfigured — that must only be able to fail
    // this endpoint, never the rest of the worlds surface.
    [HttpPost("demo")]
    public async Task<IActionResult> CreateDemo(
        [FromBody] CreateDemoWorldRequest request,
        [FromServices] IDemoWorldService demoWorldService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await demoWorldService.CreateAsync(
            new CreateDemoWorldCommand(user.Id, request.Tutorial), ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var world = result.Value!;
        var response = ToWorldResponse(world, WorldRole.GM);

        return CreatedAtAction(nameof(GetById), new { worldId = world.Id }, response);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        var result = await _worldService.ListForUserAsync(user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var worlds = result.Value!;
        var response = worlds.Select(c => new WorldListItemResponse(
            Id: c.World.Id,
            Name: c.World.Name,
            Description: c.World.Description,
            GameSystem: c.World.GameSystem,
            MyRole: c.Role.ToString(),
            PublicSlug: c.World.PublicSlug,
            PublicAccessEnabled: c.World.PublicAccessEnabled,
            DailyAiBudgetUsd: c.World.DailyAiBudgetUsd,
            PublicAskMonthlyBudgetUsd: c.World.PublicAskMonthlyBudgetUsd,
            IsDemo: c.World.IsDemo,
            TutorialEnabled: c.World.TutorialEnabled)).ToList();

        return Ok(response);
    }

    [HttpGet("{worldId:guid}")]
    [ServiceFilter(typeof(WorldMemberActionFilter))]
    public async Task<IActionResult> GetById(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _worldService.GetByIdAsync(worldId, user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var world = result.Value!;
        var response = ToWorldResponse(world, member.Role);

        return Ok(response);
    }

    [HttpPut("{worldId:guid}")]
    [ServiceFilter(typeof(WorldMemberActionFilter))]
    public async Task<IActionResult> Update(
        Guid worldId,
        [FromBody] UpdateWorldRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can update world settings."));
        }

        var command = new UpdateWorldCommand(
            WorldId: worldId,
            Name: request.Name,
            Description: request.Description,
            GameSystem: request.GameSystem,
            ActingUserId: user.Id,
            DailyAiBudgetUsd: request.DailyAiBudgetUsd,
            ClearDailyAiBudget: request.ClearDailyAiBudget,
            PublicSlug: request.PublicSlug,
            PublicAccessEnabled: request.PublicAccessEnabled,
            PublicAskMonthlyBudgetUsd: request.PublicAskMonthlyBudgetUsd,
            ClearPublicAskBudget: request.ClearPublicAskBudget);

        var result = await _worldService.UpdateAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var world = result.Value!;
        var response = ToWorldResponse(world, member.Role);

        return Ok(response);
    }

    // IWorldDeletionService is action-injected (not a constructor dependency) because it
    // needs blob storage, whose DI stub throws when unconfigured — that must only be able
    // to fail the delete endpoint, never the rest of the worlds surface.
    [HttpDelete("{worldId:guid}")]
    [ServiceFilter(typeof(WorldMemberActionFilter))]
    public async Task<IActionResult> Delete(
        Guid worldId,
        [FromQuery] string? confirmName,
        [FromServices] IWorldDeletionService deletionService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can delete a world."));
        }

        var command = new DeleteWorldCommand(
            WorldId: worldId,
            ActingUserId: user.Id,
            ConfirmationName: confirmName);

        var result = await deletionService.DeleteAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return NoContent();
    }

    // IWorldExportService is action-injected for the same reason as deletion: it depends
    // on blob storage, whose DI stub throws when unconfigured.
    [HttpPost("{worldId:guid}/export")]
    [ServiceFilter(typeof(WorldMemberActionFilter))]
    public async Task<IActionResult> Export(
        Guid worldId,
        [FromBody] ExportWorldRequest request,
        [FromServices] IWorldExportService exportService,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (member.Role != WorldRole.GM)
        {
            return StatusCode(403, new ErrorResponse("insufficient_role", "Only GMs can export a world."));
        }

        var categories = new List<WorldExportCategory>();
        foreach (var name in request.Categories ?? [])
        {
            if (!Enum.TryParse<WorldExportCategory>(name, ignoreCase: true, out var category))
            {
                return BadRequest(new ErrorResponse("invalid_category", $"'{name}' is not an exportable data type."));
            }

            categories.Add(category);
        }

        var command = new ExportWorldCommand(
            WorldId: worldId,
            ActingUserId: user.Id,
            Categories: categories);

        var result = await exportService.ExportAsync(command, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        var export = result.Value!;
        return Ok(new ExportWorldResponse(export.DownloadUrl, export.FileName, export.SizeBytes));
    }

    private static WorldResponse ToWorldResponse(World world, WorldRole role)
    {
        return new WorldResponse(
            Id: world.Id,
            Name: world.Name,
            Description: world.Description,
            GameSystem: world.GameSystem,
            CreatedByUserId: world.CreatedByUserId,
            CreatedAt: world.CreatedAt,
            UpdatedAt: world.UpdatedAt,
            MyRole: role.ToString(),
            DailyAiBudgetUsd: world.DailyAiBudgetUsd,
            PublicSlug: world.PublicSlug,
            PublicAccessEnabled: world.PublicAccessEnabled,
            PublicAskMonthlyBudgetUsd: world.PublicAskMonthlyBudgetUsd,
            IsDemo: world.IsDemo,
            TutorialEnabled: world.TutorialEnabled);
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
