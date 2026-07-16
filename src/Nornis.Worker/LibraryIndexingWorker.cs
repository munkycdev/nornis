using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Infrastructure.Messaging;

namespace Nornis.Worker;

/// <summary>
/// Background service for the library-indexing queue: deserializes the message, runs
/// <see cref="ILibraryIndexingService"/> in its own DI scope, and completes/abandons by
/// outcome — the same thin shape as <see cref="ExtractionWorker"/>.
/// </summary>
public sealed class LibraryIndexingWorker : BackgroundService
{
    /// <summary>Keyed-service key for this worker's queue processor — the extraction worker
    /// owns the unkeyed registration.</summary>
    public const string ProcessorKey = "library-indexing";

    private readonly ServiceBusExtractionProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LibraryIndexingWorker> _logger;

    public LibraryIndexingWorker(
        [FromKeyedServices(ProcessorKey)] ServiceBusExtractionProcessor processor,
        IServiceScopeFactory scopeFactory,
        ILogger<LibraryIndexingWorker> logger)
    {
        _processor = processor;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("LibraryIndexingWorker started, listening on {Queue}", ServiceBusLibraryIndexingQueueClient.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown requested
        }

        await _processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var stopwatch = Stopwatch.StartNew();

        LibraryIndexingMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<LibraryIndexingMessage>(args.Message.Body.ToString());
            if (message is null || message.DocumentId == Guid.Empty || message.WorldId == Guid.Empty)
            {
                _logger.LogError("Invalid library indexing message: {Body}", args.Message.Body.ToString());
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Undeserializable library indexing message: {Body}", args.Message.Body.ToString());
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var indexingService = scope.ServiceProvider.GetRequiredService<ILibraryIndexingService>();

            var outcome = await indexingService.ProcessIndexingAsync(message.DocumentId, message.WorldId, args.CancellationToken);
            stopwatch.Stop();

            if (outcome.Type == OutcomeType.TransientFailure)
            {
                _logger.LogWarning(
                    "Library indexing transient failure, abandoning for redelivery. DocumentId={DocumentId}, Error={Error}, DurationMs={DurationMs}",
                    message.DocumentId, outcome.ErrorMessage, stopwatch.ElapsedMilliseconds);
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                return;
            }

            _logger.LogInformation(
                "Library indexing finished. DocumentId={DocumentId}, Outcome={Outcome}, Chunks={Chunks}, DurationMs={DurationMs}, Error={Error}",
                message.DocumentId, outcome.Type, outcome.ProposalCount, stopwatch.ElapsedMilliseconds, outcome.ErrorMessage);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected exception during library indexing. DocumentId={DocumentId}, DurationMs={DurationMs}",
                message.DocumentId, stopwatch.ElapsedMilliseconds);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processor error. ErrorSource={ErrorSource}, EntityPath={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
