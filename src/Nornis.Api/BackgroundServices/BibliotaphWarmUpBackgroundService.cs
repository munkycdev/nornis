using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;

namespace Nornis.Api.BackgroundServices;

/// <summary>
/// Sends the warm-ups <see cref="BibliotaphWarmUps"/> queued, one at a time, through each world
/// creator's connection. A failed warm-up is forgotten: the question that follows reports
/// "opening" if the library still is.
/// </summary>
public class BibliotaphWarmUpBackgroundService : BackgroundService
{
    private readonly BibliotaphWarmUps _warmUps;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BibliotaphOptions _options;
    private readonly ILogger<BibliotaphWarmUpBackgroundService> _logger;

    public BibliotaphWarmUpBackgroundService(
        BibliotaphWarmUps warmUps,
        IServiceScopeFactory scopeFactory,
        IOptions<BibliotaphOptions> options,
        ILogger<BibliotaphWarmUpBackgroundService> logger)
    {
        _warmUps = warmUps;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            return;
        }

        try
        {
            await foreach (var worldId in _warmUps.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var owner = await scope.ServiceProvider.GetRequiredService<IBibliotaphConnectionResolver>()
                        .ForWorldAsync(worldId, stoppingToken);
                    if (owner?.Connection is not null)
                    {
                        await scope.ServiceProvider.GetRequiredService<IBibliotaphClient>().WarmAsync(owner.UserId, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Bibliotaph warm-up for world {WorldId} failed", worldId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
