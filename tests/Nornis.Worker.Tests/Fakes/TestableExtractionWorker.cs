using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Worker.Tests.Fakes;

namespace Nornis.Worker.Tests;

/// <summary>
/// A testable version of ExtractionWorker's message handling logic.
/// Replicates the exact behavior of <see cref="ExtractionWorker.ProcessMessageAsync"/>
/// but operates on <see cref="FakeMessageContext"/> instead of
/// <see cref="Azure.Messaging.ServiceBus.ProcessMessageEventArgs"/>.
/// This allows unit testing the message handling logic without
/// requiring Azure Service Bus infrastructure.
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
    public async Task InvokeProcessMessageAsync(FakeMessageContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        var cancellationToken = CancellationToken.None;

        ExtractionMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ExtractionMessage>(context.MessageBody);
            if (message is null || message.SourceId == Guid.Empty || message.CampaignId == Guid.Empty)
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
            "Processing extraction message. CorrelationId={CorrelationId}, SourceId={SourceId}, CampaignId={CampaignId}",
            correlationId,
            message.SourceId,
            message.CampaignId);

        try
        {
            var outcome = await _extractionService.ProcessExtractionAsync(
                message.SourceId, message.CampaignId, cancellationToken);

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
                    await context.CompleteMessageAsync(cancellationToken);
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
                    await context.CompleteMessageAsync(cancellationToken);
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
                    await context.CompleteMessageAsync(cancellationToken);
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
                    await context.AbandonMessageAsync(cancellationToken);
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

            await context.AbandonMessageAsync(cancellationToken);
        }
    }
}
