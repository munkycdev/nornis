namespace Nornis.Api.Contracts.Responses;

public record TimePeriodSummaryResponse(
    CostSummaryResponse Today,
    CostSummaryResponse ThisWeek,
    CostSummaryResponse ThisMonth,
    CostSummaryResponse AllTime);
