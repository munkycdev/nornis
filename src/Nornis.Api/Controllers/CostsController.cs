using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Models;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/costs")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class CostsController : ControllerBase
{
    private readonly ICostService _costService;
    private readonly ILogger<CostsController> _logger;
    private readonly IAiBudgetGuard _budgetGuard;

    public CostsController(
        ICostService costService,
        ILogger<CostsController> logger,
        IAiBudgetGuard budgetGuard)
    {
        _budgetGuard = budgetGuard;
        _costService = costService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        _logger.LogInformation(
            "Cost summary requested. WorldId={WorldId}, UserId={UserId}, CorrelationId={CorrelationId}",
            worldId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetSummaryAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        // Budget display honors any per-world override.
        var budgetStatus = await _budgetGuard.GetStatusAsync(worldId, ct);
        return Ok(ToTimePeriodSummaryResponse(result.Value!, budgetStatus.DailyBudgetUsd));
    }

    [HttpGet("by-user")]
    public async Task<IActionResult> GetByUser(
        Guid worldId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        _logger.LogInformation(
            "Cost by-user requested. WorldId={WorldId}, UserId={UserId}, CorrelationId={CorrelationId}",
            worldId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByUserAsync(
            worldId, user.Id, member.Role, startDate, endDate, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToUserCostResponse).ToList());
    }

    [HttpGet("by-operation")]
    public async Task<IActionResult> GetByOperation(
        Guid worldId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        _logger.LogInformation(
            "Cost by-operation requested. WorldId={WorldId}, UserId={UserId}, CorrelationId={CorrelationId}",
            worldId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByOperationTypeAsync(
            worldId, user.Id, member.Role, startDate, endDate, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToOperationTypeCostResponse).ToList());
    }

    [HttpGet("by-model")]
    public async Task<IActionResult> GetByModel(
        Guid worldId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        _logger.LogInformation(
            "Cost by-model requested. WorldId={WorldId}, UserId={UserId}, CorrelationId={CorrelationId}",
            worldId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByModelAsync(
            worldId, user.Id, member.Role, startDate, endDate, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToModelCostResponse).ToList());
    }

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(500, new ErrorResponse("internal_error", "Something went wrong. Please try again."))
        };
    }

    private static TimePeriodSummaryResponse ToTimePeriodSummaryResponse(TimePeriodCostResult result, decimal budget)
    {
        return new TimePeriodSummaryResponse(
            DailyBudgetUsd: budget > 0 ? budget : null,
            Today: ToCostSummaryResponse(result.Today),
            ThisWeek: ToCostSummaryResponse(result.ThisWeek),
            ThisMonth: ToCostSummaryResponse(result.ThisMonth),
            AllTime: ToCostSummaryResponse(result.AllTime));
    }

    private static UserCostResponse ToUserCostResponse(UserCostResult result)
    {
        return new UserCostResponse(
            UserId: result.UserId,
            Username: result.Username,
            Summary: ToCostSummaryResponse(result.Summary));
    }

    private static OperationTypeCostResponse ToOperationTypeCostResponse(OperationTypeCostResult result)
    {
        return new OperationTypeCostResponse(
            OperationType: result.OperationType,
            Summary: ToCostSummaryResponse(result.Summary));
    }

    private static ModelCostResponse ToModelCostResponse(ModelCostResult result)
    {
        return new ModelCostResponse(
            Model: result.Model,
            Summary: ToCostSummaryResponse(result.Summary));
    }

    private static CostSummaryResponse ToCostSummaryResponse(CostSummary summary)
    {
        return new CostSummaryResponse(
            TotalInputTokens: summary.TotalInputTokens,
            TotalOutputTokens: summary.TotalOutputTokens,
            TotalTokens: summary.TotalTokens,
            TotalEstimatedCostUsd: summary.TotalEstimatedCostUsd,
            OperationCount: summary.OperationCount);
    }
}

/// <summary>
/// Cross-world cost endpoint. Not scoped to a single world and does not require
/// world membership validation — only an authenticated user (resolved by UserProvisioningMiddleware).
/// The service internally queries all worlds where the user holds the GM role.
/// </summary>
[ApiController]
[Route("api/costs")]
public class CrossWorldCostsController : ControllerBase
{
    private readonly ICostService _costService;
    private readonly ILogger<CrossWorldCostsController> _logger;

    public CrossWorldCostsController(ICostService costService, ILogger<CrossWorldCostsController> logger)
    {
        _costService = costService;
        _logger = logger;
    }

    [HttpGet("by-world")]
    public async Task<IActionResult> GetByWorld(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        _logger.LogInformation(
            "Cost by-world requested. UserId={UserId}, CorrelationId={CorrelationId}",
            user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByWorldAsync(user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToWorldCostResponse).ToList());
    }

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(500, new ErrorResponse("internal_error", "Something went wrong. Please try again."))
        };
    }

    private static WorldCostResponse ToWorldCostResponse(WorldCostResult result)
    {
        return new WorldCostResponse(
            WorldId: result.WorldId,
            WorldName: result.WorldName,
            Summary: ToCostSummaryResponse(result.Summary));
    }

    private static CostSummaryResponse ToCostSummaryResponse(CostSummary summary)
    {
        return new CostSummaryResponse(
            TotalInputTokens: summary.TotalInputTokens,
            TotalOutputTokens: summary.TotalOutputTokens,
            TotalTokens: summary.TotalTokens,
            TotalEstimatedCostUsd: summary.TotalEstimatedCostUsd,
            OperationCount: summary.OperationCount);
    }
}
