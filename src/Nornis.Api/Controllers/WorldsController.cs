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
            PublicAskMonthlyBudgetUsd: c.World.PublicAskMonthlyBudgetUsd)).ToList();

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
            PublicAskMonthlyBudgetUsd: world.PublicAskMonthlyBudgetUsd);
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
