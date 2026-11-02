using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;

namespace Nornis.Api.BackgroundServices;

/// <summary>
/// Delivers the Bibliotaph outbox (feature 31) on a short tick; see
/// <see cref="BibliotaphOutboxDelivery"/> for what one pass does.
///
/// <para>
/// In the API, not the Worker, because the Worker scales to zero when its queues are empty and
/// a rename would wait for an unrelated extraction to wake it. The API never scales to zero.
/// No operational pause gate: the pause stops AI spend, and this makes no AI calls.
/// </para>
/// </summary>
public class BibliotaphOutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BibliotaphOptions _options;
    private readonly ILogger<BibliotaphOutboxBackgroundService> _logger;

    public BibliotaphOutboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<BibliotaphOptions> options,
        ILogger<BibliotaphOutboxBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogInformation("Bibliotaph outbox off: Bibliotaph isn't configured");
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.OutboxPollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Delay first, as the upload sweep does, so an incoming revision doesn't deliver
            // alongside the outgoing one on its first breath.
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
                await scope.ServiceProvider.GetRequiredService<IBibliotaphOutboxDelivery>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed pass is a retry next tick, never a dead loop.
                _logger.LogError(ex, "Bibliotaph outbox pass failed");
            }
        }
    }
}
