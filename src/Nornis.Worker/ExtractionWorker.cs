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
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("ExtractionWorker started, listening for messages on source-extraction queue");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown requested
        }

        await _processor.StopProcessingAsync(CancellationToken.None);

        _logger.LogInformation("ExtractionWorker stopped");
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        ExtractionMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ExtractionMessage>(args.Message.Body.ToString());
            if (message is null || message.SourceId == Guid.Empty || message.CampaignId == Guid.Empty)
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
            "Processing extraction message. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}",
            correlationId,
            message.SourceId,
            message.CampaignId);

        try
        {
            // One DI scope per message: IExtractionService (and its DbContext) are scoped,
            // and messages may process concurrently — they must never share a context.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var extractionService = scope.ServiceProvider.GetRequiredService<IExtractionService>();

            var outcome = await extractionService.ProcessExtractionAsync(
                message.SourceId, message.CampaignId, args.CancellationToken);

            stopwatch.Stop();

            switch (outcome.Type)
            {
                case OutcomeType.Success:
                    _logger.LogInformation(
                        "Extraction succeeded. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, ReviewBatchId={ReviewBatchId}, ProposalCount={ProposalCount}",
                        correlationId,
                        message.SourceId,
                        message.CampaignId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ReviewBatchId,
                        outcome.ProposalCount);
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                    break;

                case OutcomeType.Skipped:
                    _logger.LogInformation(
                        "Extraction skipped. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, Reason={Reason}",
                        correlationId,
                        message.SourceId,
                        message.CampaignId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ErrorMessage);
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                    break;

                case OutcomeType.NonTransientFailure:
                    _logger.LogError(
                        "Extraction failed with non-transient error. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, ErrorCategory={ErrorCategory}, ErrorMessage={ErrorMessage}",
                        correlationId,
                        message.SourceId,
                        message.CampaignId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ErrorCategory,
                        outcome.ErrorMessage);
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                    break;

                case OutcomeType.TransientFailure:
                    _logger.LogWarning(
                        "Extraction failed with transient error, abandoning message for redelivery. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}, OutcomeType={OutcomeType}, DurationMs={DurationMs}, ErrorCategory={ErrorCategory}, ErrorMessage={ErrorMessage}",
                        correlationId,
                        message.SourceId,
                        message.CampaignId,
                        outcome.Type,
                        stopwatch.ElapsedMilliseconds,
                        outcome.ErrorCategory,
                        outcome.ErrorMessage);
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Unexpected exception during extraction processing. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}, DurationMs={DurationMs}",
                correlationId,
                message.SourceId,
                message.CampaignId,
                stopwatch.ElapsedMilliseconds);

            // Unexpected exceptions are treated as transient — abandon for redelivery.
            // If the issue persists, Service Bus will move it to the dead-letter queue
            // after max delivery count is exceeded.
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
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
