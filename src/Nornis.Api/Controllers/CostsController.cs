using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Models;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/campaigns/{campaignId:guid}/costs")]
[ServiceFilter(typeof(CampaignMemberActionFilter))]
public class CostsController : ControllerBase
{
    private readonly ICostService _costService;
    private readonly ILogger<CostsController> _logger;

    public CostsController(ICostService costService, ILogger<CostsController> logger)
    {
        _costService = costService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(Guid campaignId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        _logger.LogInformation(
            "Cost summary requested. CampaignId={CampaignId}, UserId={UserId}, CorrelationId={CorrelationId}",
            campaignId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetSummaryAsync(campaignId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(ToTimePeriodSummaryResponse(result.Value!));
    }

    [HttpGet("by-user")]
    public async Task<IActionResult> GetByUser(
        Guid campaignId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        _logger.LogInformation(
            "Cost by-user requested. CampaignId={CampaignId}, UserId={UserId}, CorrelationId={CorrelationId}",
            campaignId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByUserAsync(
            campaignId, user.Id, member.Role, startDate, endDate, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToUserCostResponse).ToList());
    }

    [HttpGet("by-operation")]
    public async Task<IActionResult> GetByOperation(
        Guid campaignId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        _logger.LogInformation(
            "Cost by-operation requested. CampaignId={CampaignId}, UserId={UserId}, CorrelationId={CorrelationId}",
            campaignId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByOperationTypeAsync(
            campaignId, user.Id, member.Role, startDate, endDate, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToOperationTypeCostResponse).ToList());
    }

    [HttpGet("by-model")]
    public async Task<IActionResult> GetByModel(
        Guid campaignId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetCampaignMember();

        _logger.LogInformation(
            "Cost by-model requested. CampaignId={CampaignId}, UserId={UserId}, CorrelationId={CorrelationId}",
            campaignId, user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByModelAsync(
            campaignId, user.Id, member.Role, startDate, endDate, ct);

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

    private static TimePeriodSummaryResponse ToTimePeriodSummaryResponse(TimePeriodCostResult result)
    {
        return new TimePeriodSummaryResponse(
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
/// Cross-campaign cost endpoint. Not scoped to a single campaign and does not require
/// campaign membership validation — only an authenticated user (resolved by UserProvisioningMiddleware).
/// The service internally queries all campaigns where the user holds the GM role.
/// </summary>
[ApiController]
[Route("api/costs")]
public class CrossCampaignCostsController : ControllerBase
{
    private readonly ICostService _costService;
    private readonly ILogger<CrossCampaignCostsController> _logger;

    public CrossCampaignCostsController(ICostService costService, ILogger<CrossCampaignCostsController> logger)
    {
        _costService = costService;
        _logger = logger;
    }

    [HttpGet("by-campaign")]
    public async Task<IActionResult> GetByCampaign(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();

        _logger.LogInformation(
            "Cost by-campaign requested. UserId={UserId}, CorrelationId={CorrelationId}",
            user.Id, HttpContext.TraceIdentifier);

        var result = await _costService.GetByCampaignAsync(user.Id, ct);

        if (!result.IsSuccess)
        {
            return MapError(result.Error!);
        }

        return Ok(result.Value!.Select(ToCampaignCostResponse).ToList());
    }

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(500, new ErrorResponse("internal_error", "Something went wrong. Please try again."))
        };
    }

    private static CampaignCostResponse ToCampaignCostResponse(CampaignCostResult result)
    {
        return new CampaignCostResponse(
            CampaignId: result.CampaignId,
            CampaignName: result.CampaignName,
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
