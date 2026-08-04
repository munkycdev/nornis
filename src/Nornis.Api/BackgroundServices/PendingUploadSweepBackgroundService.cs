using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;

namespace Nornis.Api.BackgroundServices;

/// <summary>
/// Ticks on an interval and removes upload rows whose blob never arrived — see
/// <see cref="PendingUploadSweeper"/> for what makes one abandoned rather than in flight.
///
/// <para>
/// Unlike the continuity audit's tick, this needs no claim to be safe against two hosts running
/// it at once. The sweep is idempotent by construction: it deletes the blob first and the row
/// second, and a row another replica already removed simply is not in the next query. The worst
/// a race costs is a duplicate storage delete, which storage answers the same way twice.
/// </para>
/// </summary>
public class PendingUploadSweepBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UploadSweepOptions _options;
    private readonly ILogger<PendingUploadSweepBackgroundService> _logger;

    public PendingUploadSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<UploadSweepOptions> options,
        ILogger<PendingUploadSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Non-positive is off, not "as fast as possible" — the same reading the continuity tick
        // settled on after a configured 0 turned into a delay-free loop.
        if (_options.TickIntervalHours <= 0)
        {
            _logger.LogInformation(
                "Abandoned-upload sweep disabled (UploadSweep:TickIntervalHours={Interval})",
                _options.TickIntervalHours);
            return;
        }

        var interval = TimeSpan.FromHours(_options.TickIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Delay first, so a rolling deploy does not have the incoming revision sweeping
            // while the outgoing one is still draining.
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPendingUploadSweeper>()
                    .SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep is a retry next tick, never a dead loop.
                _logger.LogError(ex, "Abandoned-upload sweep failed");
            }
        }
    }
}
