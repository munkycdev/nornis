using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Infrastructure.Messaging;

namespace Nornis.Worker;

/// <summary>
/// Background service that receives extraction messages from Azure Service Bus
/// and delegates processing to <see cref="IExtractionService"/>. The worker is thin:
/// it deserializes the message, calls the service, and decides whether to complete
/// or abandon the message based on the outcome.
/// </summary>
public sealed class ExtractionWorker : BackgroundService
{
    private readonly ServiceBusExtractionProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtractionWorker> _logger;

    public ExtractionWorker(
        ServiceBusExtractionProcessor processor,
        IServiceScopeFactory scopeFactory,
        ILogger<ExtractionWorker> logger)
    {
        _processor = processor;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        if (!await ProcessorStartup.StartWithRetryAsync(
                _processor.StartProcessingAsync, "source-extraction", _logger, stoppingToken))
        {
            return;
        }

        _logger.LogInformation("ExtractionWorker started, listening for messages on source-extraction queue");

        await PausableProcessing.RunUntilStoppedAsync(
            _processor.StartProcessingAsync,
            _processor.StopProcessingAsync,
            ReadPauseStateAsync,
            "source-extraction",
            _logger,
            stoppingToken);

        _logger.LogInformation("ExtractionWorker stopped");
    }

    /// <summary>
    /// A fresh scope per poll: the gate's repository is scoped to a DbContext, and a worker
    /// that lives for days must not hold one open for the duration.
    /// </summary>
    private async Task<AiPauseState> ReadPauseStateAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAiPauseGate>().GetAsync(ct);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        ExtractionMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ExtractionMessage>(args.Message.Body.ToString());
            if (message is null || message.SourceId == Guid.Empty || message.WorldId == Guid.Empty)
            {
                _logger.LogError(
                    "Deserialization returned null or invalid message. CorrelationId={CorrelationId}, Body={MessageBody}",
                    correlationId,
                    args.Message.Body.ToString());

                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to deserialize extraction message. CorrelationId={CorrelationId}, Body={MessageBody}",
                correlationId,
                args.Message.Body.ToString());

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        _logger.LogInformation(
            "Processing extraction message. CorrelationId={CorrelationId}, SourceId={SourceId}, WorldId={WorldId}, Kind={Kind}",
            correlationId,
            message.SourceId,
            message.WorldId,
            message.Kind);

        try
        {
            // One DI scope per message: the services (and their DbContext) are scoped,
            // and messages may process concurrently — they must never share a context.
            await using var scope = _scopeFactory.CreateAsyncScope();

            var outcome = message.Kind == ExtractionKind.RelationshipBackfill
                ? await scope.ServiceProvider.GetRequiredService<IRelationshipBackfillService>()
                    .ProcessBackfillAsync(message.SourceId, message.WorldId, args.CancellationToken)
                : await scope.ServiceProvider.GetRequiredService<IExtractionService>()
                    .ProcessExtractionAsync(message.SourceId, message.WorldId, args.CancellationToken);

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
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
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
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
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
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
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
                    await RedeliveryBackoff.DelayThenAbandonAsync(args, (delay, attempt) =>
                        _logger.LogInformation(
                            "Backing off {DelaySeconds}s before redelivery (attempt {Attempt}). CorrelationId={CorrelationId}, SourceId={SourceId}",
                            delay.TotalSeconds, attempt, correlationId, message.SourceId));
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

            // Unexpected exceptions are treated as transient — abandon for redelivery, after the
            // same backoff. If the issue persists, Service Bus dead-letters the message once max
            // delivery count is exceeded; the backoff makes that take minutes rather than
            // milliseconds, which is the difference between riding out an outage and amplifying it.
            await RedeliveryBackoff.DelayThenAbandonAsync(args, (delay, attempt) =>
                _logger.LogInformation(
                    "Backing off {DelaySeconds}s before redelivery (attempt {Attempt}). CorrelationId={CorrelationId}, SourceId={SourceId}",
                    delay.TotalSeconds, attempt, correlationId, message.SourceId));
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus processor error. ErrorSource={ErrorSource}, EntityPath={EntityPath}",
            args.ErrorSource,
            args.EntityPath);

        return Task.CompletedTask;
    }
}
