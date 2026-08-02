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

        if (!await ProcessorStartup.StartWithRetryAsync(
                _processor.StartProcessingAsync, ServiceBusLibraryIndexingQueueClient.QueueName, _logger, stoppingToken))
        {
            return;
        }

        _logger.LogInformation("LibraryIndexingWorker started, listening on {Queue}", ServiceBusLibraryIndexingQueueClient.QueueName);

        await PausableProcessing.RunUntilStoppedAsync(
            _processor.StartProcessingAsync,
            _processor.StopProcessingAsync,
            ReadPauseStateAsync,
            ServiceBusLibraryIndexingQueueClient.QueueName,
            _logger,
            stoppingToken);
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
                await RedeliveryBackoff.DelayThenAbandonAsync(args, (delay, attempt) =>
                    _logger.LogInformation(
                        "Backing off {DelaySeconds}s before redelivery (attempt {Attempt}). DocumentId={DocumentId}",
                        delay.TotalSeconds, attempt, message.DocumentId));
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
            await RedeliveryBackoff.DelayThenAbandonAsync(args, (delay, attempt) =>
                _logger.LogInformation(
                    "Backing off {DelaySeconds}s before redelivery (attempt {Attempt}). DocumentId={DocumentId}",
                    delay.TotalSeconds, attempt, message.DocumentId));
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
