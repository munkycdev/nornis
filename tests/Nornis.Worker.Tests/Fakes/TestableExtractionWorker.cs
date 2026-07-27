using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Worker.Tests.Fakes;

namespace Nornis.Worker.Tests;

/// <summary>
/// A testable version of ExtractionWorker's message handling logic, operating on
/// <see cref="FakeMessageContext"/> instead of the sealed
/// <see cref="Azure.Messaging.ServiceBus.ProcessMessageEventArgs"/>, which cannot be constructed
/// in a test.
///
/// <para><b>This is a hand-maintained copy, and copies drift.</b> It had already fallen behind
/// once: when the production handler gained a redelivery backoff, this kept abandoning
/// immediately, so the tests below went on passing while asserting on a shape that no longer ran.
/// Anything changed in <c>ExtractionWorker.ProcessMessageAsync</c> has to be mirrored here by
/// hand, and the compiler will not tell you. Prefer testing extracted helpers — such as
/// <see cref="RedeliveryBackoff"/> — over widening what this duplicates.</para>
/// </summary>
public sealed class TestableExtractionWorker
{
    private readonly IExtractionService _extractionService;
    private readonly ILogger<ExtractionWorker> _logger;

    public TestableExtractionWorker(
        IExtractionService extractionService,
        ILogger<ExtractionWorker> logger)
    {
        _extractionService = extractionService;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the same message handling logic as ExtractionWorker.ProcessMessageAsync
    /// but using a <see cref="FakeMessageContext"/> for testability.
    /// </summary>
    public async Task InvokeProcessMessageAsync(
        FakeMessageContext context, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        ExtractionMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ExtractionMessage>(context.MessageBody);
            if (message is null || message.SourceId == Guid.Empty || message.WorldId == Guid.Empty)
            {
                _logger.LogError(
                    "Deserialization returned null or invalid message. CorrelationId={CorrelationId}, Body={MessageBody}",
                    correlationId,
                    context.MessageBody);

                await context.CompleteMessageAsync(cancellationToken);
                return;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to deserialize extraction message. CorrelationId={CorrelationId}, Body={MessageBody}",
                correlationId,
                context.MessageBody);

            await context.CompleteMessageAsync(cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Processing extraction message. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}",
            correlationId,
            message.SourceId,
            message.WorldId);

        try
        {
            var outcome = await _extractionService.ProcessExtractionAsync(
                message.SourceId, message.WorldId, cancellationToken);

            stopwatch.Stop();

            switch (outcome.Type)
            {
                case OutcomeType.Success:
                    _logger.LogInformation(
                        "Extraction succeeded. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, ReviewBatchId={ReviewBatchId}, ProposalCount={ProposalCount}",
                        correlationId,
                        message.SourceId,
                        message.WorldId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ReviewBatchId,
                        outcome.ProposalCount);
                    await context.CompleteMessageAsync(cancellationToken);
                    break;

                case OutcomeType.Skipped:
                    _logger.LogInformation(
                        "Extraction skipped. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, Reason={Reason}",
                        correlationId,
                        message.SourceId,
                        message.WorldId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ErrorMessage);
                    await context.CompleteMessageAsync(cancellationToken);
                    break;

                case OutcomeType.NonTransientFailure:
                    _logger.LogError(
                        "Extraction failed with non-transient error. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, ErrorCategory={ErrorCategory}, ErrorMessage={ErrorMessage}",
                        correlationId,
                        message.SourceId,
                        message.WorldId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ErrorCategory,
                        outcome.ErrorMessage);
                    await context.CompleteMessageAsync(cancellationToken);
                    break;

                case OutcomeType.TransientFailure:
                    _logger.LogWarning(
                        "Extraction failed with transient error, abandoning message for redelivery. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, ErrorCategory={ErrorCategory}, ErrorMessage={ErrorMessage}",
                        correlationId,
                        message.SourceId,
                        message.WorldId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ErrorCategory,
                        outcome.ErrorMessage);
                    await RedeliveryBackoff.WaitAsync(context.DeliveryCount, cancellationToken, (delay, _) => context.RecordBackoff(delay));
                    await context.AbandonMessageAsync(cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Unexpected exception during extraction processing. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}, DurationMs={DurationMs}",
                correlationId,
                message.SourceId,
                message.WorldId,
                stopwatch.ElapsedMilliseconds);

            await RedeliveryBackoff.WaitAsync(context.DeliveryCount, cancellationToken, (delay, _) => context.RecordBackoff(delay));
            await context.AbandonMessageAsync(cancellationToken);
        }
    }
}
