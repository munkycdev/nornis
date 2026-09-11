using Nornis.Domain.Repositories;

namespace Nornis.Api.BackgroundServices;

/// <summary>
/// Gives rows written before slugs existed their slug, once, shortly after the host starts.
///
/// A background service rather than a migration step because the slug rule is C# — deriving it
/// again in T-SQL would be the rule in two places, and the migration would have to be kept in
/// step with <c>Slug.From</c> forever. Runs to completion and exits; every later start finds
/// nothing to do and costs one query per kind. Until it has run, links fall back to ids, so a
/// host that races another through a rolling deploy loses nothing but a log line.
/// </summary>
public class SlugBackfillBackgroundService : BackgroundService
{
    /// <summary>Lets the host finish coming up before the first write, as the sweeps do.</summary>
    internal static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlugBackfillBackgroundService> _logger;
    private readonly TimeSpan _delay;

    public SlugBackfillBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SlugBackfillBackgroundService> logger)
        : this(scopeFactory, logger, StartupDelay)
    {
    }

    public SlugBackfillBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SlugBackfillBackgroundService> logger,
        TimeSpan delay)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _delay = delay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var assigned = await scope.ServiceProvider.GetRequiredService<ISlugBackfiller>()
                .BackfillAsync(stoppingToken);

            if (assigned > 0)
            {
                _logger.LogInformation("Slug backfill assigned {Count} slugs", assigned);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The next host start retries; nothing else depends on this having run.
            _logger.LogError(ex, "Slug backfill failed");
        }
    }
}
