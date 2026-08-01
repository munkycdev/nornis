using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence;

namespace Nornis.Worker;

/// <summary>
/// Writes "still here" to the database on a fixed interval. The API's worker-heartbeat
/// status check reads the freshness — see <see cref="WorkerHeartbeatHealthCheck"/>.
///
/// One beat covers the whole process, not one per queue processor: extraction and library
/// indexing ship together, and what the status page needs to answer is whether the worker
/// is alive at all.
/// </summary>
public class HeartbeatWorker : BackgroundService
{
    /// <summary>
    /// Half the check's Degraded threshold, so a single missed or slow beat never trips it —
    /// it takes a genuinely stopped worker to age the row out.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Beat immediately rather than waiting out the first interval, so a restarted worker
        // stops looking dead as soon as it is not.
        while (!stoppingToken.IsCancellationRequested)
        {
            await BeatAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Protected so tests can drive one beat without the host lifecycle.</summary>
    protected async Task BeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IWorkerHeartbeatRepository>();
            await repository.BeatAsync(
                WorkerHeartbeatHealthCheck.WorkerName,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A heartbeat that cannot be written is worth knowing about but is never worth
            // stopping extraction for — and a database outage that prevents the write will
            // already be showing up as a failing sql check on the same page.
            _logger.LogWarning(ex, "Failed to write worker heartbeat.");
        }
    }
}
