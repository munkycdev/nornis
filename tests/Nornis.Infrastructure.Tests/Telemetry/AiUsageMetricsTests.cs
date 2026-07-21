using System.Diagnostics.Metrics;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Telemetry;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Telemetry;

[TestFixture]
public class AiUsageMetricsTests
{
    private sealed record Measurement(string Instrument, long Value, Dictionary<string, object?> Tags);

    private static List<Measurement> Capture(Action act)
    {
        var measurements = new List<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AiUsageMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (measurements)
            {
                measurements.Add(new Measurement(
                    instrument.Name, value,
                    tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
            }
        });
        listener.Start();

        act();

        return measurements;
    }

    [Test]
    public void Record_EmitsInputAndOutputTokenHistograms_WithOperationTags()
    {
        var record = new AiUsageRecord
        {
            OperationType = AiOperationType.ContinuityAudit,
            Model = "nornis-ask",
            InputTokens = 42_000,
            OutputTokens = 1_500,
            Succeeded = true
        };

        var measurements = Capture(() => AiUsageMetrics.Record(record));

        var input = measurements.Single(m => m.Instrument == "nornis.ai.input_tokens");
        Assert.That(input.Value, Is.EqualTo(42_000));
        Assert.That(input.Tags["operation_type"], Is.EqualTo("ContinuityAudit"));
        Assert.That(input.Tags["model"], Is.EqualTo("nornis-ask"));
        Assert.That(input.Tags["succeeded"], Is.EqualTo(true));

        var output = measurements.Single(m => m.Instrument == "nornis.ai.output_tokens");
        Assert.That(output.Value, Is.EqualTo(1_500));
        Assert.That(output.Tags["operation_type"], Is.EqualTo("ContinuityAudit"));
    }

    [Test]
    public void Record_FailedOperation_EmitsWithSucceededFalse()
    {
        var record = new AiUsageRecord
        {
            OperationType = AiOperationType.AskLoremaster,
            Model = "nornis-ask",
            InputTokens = 0,
            OutputTokens = 0,
            Succeeded = false
        };

        var measurements = Capture(() => AiUsageMetrics.Record(record));

        var input = measurements.Single(m => m.Instrument == "nornis.ai.input_tokens");
        Assert.That(input.Tags["succeeded"], Is.EqualTo(false));
    }
}
