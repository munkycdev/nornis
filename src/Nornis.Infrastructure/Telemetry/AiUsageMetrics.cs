using System.Diagnostics;
using System.Diagnostics.Metrics;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Telemetry;

/// <summary>
/// OpenTelemetry metrics for AI usage, emitted alongside the persisted <see cref="AiUsageRecord"/>
/// rows so alert rules can watch token consumption (e.g. a ContinuityAudit prompt outgrowing the
/// model's comfortable range). Metrics are exported unsampled, unlike traces/logs, so alerts on
/// them stay reliable even if telemetry sampling is turned on later.
/// </summary>
public static class AiUsageMetrics
{
    public const string MeterName = "Nornis.Ai";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<long> InputTokens = Meter.CreateHistogram<long>(
        "nornis.ai.input_tokens", unit: "{token}",
        description: "Input (prompt) tokens per AI operation");

    private static readonly Histogram<long> OutputTokens = Meter.CreateHistogram<long>(
        "nornis.ai.output_tokens", unit: "{token}",
        description: "Output (completion) tokens per AI operation");

    public static void Record(AiUsageRecord record)
    {
        var tags = new TagList
        {
            { "operation_type", record.OperationType.ToString() },
            { "model", record.Model },
            { "succeeded", record.Succeeded }
        };

        InputTokens.Record(record.InputTokens, tags);
        OutputTokens.Record(record.OutputTokens, tags);
    }
}
