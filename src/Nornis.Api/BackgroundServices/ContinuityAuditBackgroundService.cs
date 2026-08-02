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
        // Non-positive means disabled, not "as fast as possible". The previous
        // Math.Max(0.0, ...) floor turned a configured 0 — which reads naturally as "off" —
        // into a delay-free loop over every world in the database.
        if (_options.TickIntervalHours <= 0)
        {
            _logger.LogInformation(
                "Continuity audit auto-trigger disabled (ContinuityAudit:TickIntervalHours={Interval})",
                _options.TickIntervalHours);
            return;
        }

        var interval = TimeSpan.FromHours(_options.TickIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Delay first. Ticking on startup means every deploy, restart and scale-out event
            // fires a sweep, and during a rolling deploy the incoming revision would sweep while
            // the outgoing one is still draining.
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
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var proposalRepo = sp.GetRequiredService<IReviewProposalRepository>();
        var assessmentRepo = sp.GetRequiredService<IHealthAssessmentRepository>();
        var worldRepo = sp.GetRequiredService<IWorldRepository>();
        var auditService = sp.GetRequiredService<IContinuityAuditService>();

        var quietPeriod = TimeSpan.FromHours(_options.QuietPeriodHours);
        var minInterval = TimeSpan.FromHours(_options.MinIntervalHours);
        var claimTimeout = TimeSpan.FromHours(_options.ClaimTimeoutHours);

        var worldIds = await proposalRepo.ListWorldIdsWithAcceptancesAsync(ct);

        foreach (var worldId in worldIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var latestAcceptance = await proposalRepo.GetLatestAcceptanceTimeAsync(worldId, ct);
                var latestAssessment = await assessmentRepo.GetLatestCreatedAtAsync(worldId, ct);

                var now = DateTimeOffset.UtcNow;

                if (!ContinuityAuditEligibility.IsEligible(
                        latestAcceptance, latestAssessment, now, quietPeriod, minInterval))
                {
                    continue;
                }

                // Eligibility is a read, so two hosts can reach this point for the same world.
                // The claim is the arbiter: a conditional UPDATE that exactly one caller wins.
                // Without it a rolling deploy — or any future increase to the API's max replica
                // count — pays twice for the same assessment.
                if (!await worldRepo.TryClaimContinuityAuditAsync(worldId, now, now - claimTimeout, ct))
                {
                    _logger.LogDebug(
                        "Skipping continuity assessment for world {WorldId}; another host holds the claim", worldId);
                    continue;
                }

                _logger.LogInformation("Auto-running continuity assessment for world {WorldId}", worldId);

                // System-run: no user attributed. RunAssessmentAsync records its own usage/failures.
                var result = await auditService.RunAssessmentAsync(worldId, null, actingUserRole: null, ct);
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
