using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Domain.Repositories;

namespace Nornis.Api.BackgroundServices;

/// <summary>
/// Ticks on an interval and auto-runs an AI continuity assessment for any world that is due one.
/// Eligibility is fully derivable (no dirty flags): a run happens when a world has accepted new
/// canon since its last assessment, that acceptance has settled past the quiet period, and no
/// assessment ran within the minimum interval — see <see cref="ContinuityAuditEligibility"/>.
///
/// The service is a singleton over scoped dependencies, so each tick opens its own DI scope. One
/// world's failure is caught and logged so it never kills the loop.
/// </summary>
public class ContinuityAuditBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ContinuityAuditOptions _options;
    private readonly ILogger<ContinuityAuditBackgroundService> _logger;

    public ContinuityAuditBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ContinuityAuditOptions> options,
        ILogger<ContinuityAuditBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(0.0, _options.TickIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A whole-tick failure (e.g. the candidate query) must not kill the loop.
                _logger.LogError(ex, "Continuity audit tick failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var proposalRepo = sp.GetRequiredService<IReviewProposalRepository>();
        var assessmentRepo = sp.GetRequiredService<IHealthAssessmentRepository>();
        var auditService = sp.GetRequiredService<IContinuityAuditService>();

        var quietPeriod = TimeSpan.FromHours(_options.QuietPeriodHours);
        var minInterval = TimeSpan.FromHours(_options.MinIntervalHours);

        var worldIds = await proposalRepo.ListWorldIdsWithAcceptancesAsync(ct);

        foreach (var worldId in worldIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var latestAcceptance = await proposalRepo.GetLatestAcceptanceTimeAsync(worldId, ct);
                var latestAssessment = await assessmentRepo.GetLatestCreatedAtAsync(worldId, ct);

                if (!ContinuityAuditEligibility.IsEligible(
                        latestAcceptance, latestAssessment, DateTimeOffset.UtcNow, quietPeriod, minInterval))
                {
                    continue;
                }

                _logger.LogInformation("Auto-running continuity assessment for world {WorldId}", worldId);

                // System-run: no user attributed. RunAssessmentAsync records its own usage/failures.
                var result = await auditService.RunAssessmentAsync(worldId, null, ct);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Auto continuity assessment for world {WorldId} failed: {Code} {Message}",
                        worldId, result.Error!.Code, result.Error.Message);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One world's failure must not stop the others.
                _logger.LogError(ex, "Continuity assessment for world {WorldId} threw", worldId);
            }
        }
    }
}
