using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Api.BackgroundServices;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.BackgroundServices;

/// <summary>
/// The auto-trigger spends real money — each eligible world costs an Azure OpenAI call — so the
/// loop's guard rails matter as much as its happy path. These cover the three that protect
/// against unintended spend: a disabled interval must not sweep, startup must not sweep, and a
/// world already claimed by another host must not be assessed twice.
/// </summary>
[TestFixture]
public class ContinuityAuditBackgroundServiceTests
{
    private IReviewProposalRepository _proposals = null!;
    private IHealthAssessmentRepository _assessments = null!;
    private IWorldRepository _worlds = null!;
    private IContinuityAuditService _auditService = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private int _scopesCreated;

    [SetUp]
    public void SetUp()
    {
        _proposals = Substitute.For<IReviewProposalRepository>();
        _assessments = Substitute.For<IHealthAssessmentRepository>();
        _worlds = Substitute.For<IWorldRepository>();
        _auditService = Substitute.For<IContinuityAuditService>();
        _scopesCreated = 0;

        var services = new ServiceCollection();
        services.AddSingleton(_proposals);
        services.AddSingleton(_assessments);
        services.AddSingleton(_worlds);
        services.AddSingleton(_auditService);
        var provider = services.BuildServiceProvider();

        // Counting scopes is how we observe "did a tick happen" without reaching into the loop.
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(_ =>
        {
            Interlocked.Increment(ref _scopesCreated);
            return provider.CreateScope();
        });
    }

    private ContinuityAuditBackgroundService CreateService(ContinuityAuditOptions options) =>
        new(_scopeFactory,
            Options.Create(options),
            NullLogger<ContinuityAuditBackgroundService>.Instance);

    [TestCase(0.0)]
    [TestCase(-1.0)]
    public async Task NonPositiveInterval_DisablesTheTriggerEntirely(double interval)
    {
        // A configured 0 reads as "off". The previous Math.Max(0.0, ...) floor turned it into a
        // delay-free loop that swept every world in the database as fast as SQL would answer.
        var service = CreateService(new ContinuityAuditOptions { TickIntervalHours = interval });

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.That(_scopesCreated, Is.Zero, "a disabled trigger must never open a scope or query worlds");
        await _proposals.DidNotReceive().ListWorldIdsWithAcceptancesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PositiveInterval_DoesNotSweepOnStartup()
    {
        // Ticking before the first delay meant every deploy, restart and scale-out fired a
        // sweep — and during a rolling deploy the incoming revision swept while the outgoing
        // one was still draining.
        var service = CreateService(new ContinuityAuditOptions { TickIntervalHours = 1.0 });

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        Assert.That(_scopesCreated, Is.Zero, "the first sweep must wait out the interval");
    }

    [Test]
    public async Task EligibleWorldAlreadyClaimedByAnotherHost_IsNotAssessed()
    {
        var worldId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        _proposals.ListWorldIdsWithAcceptancesAsync(Arg.Any<CancellationToken>())
            .Returns([worldId]);
        // Eligible on every count: acceptance settled past the quiet period, never assessed.
        _proposals.GetLatestAcceptanceTimeAsync(worldId, Arg.Any<CancellationToken>())
            .Returns(now.AddHours(-5));
        _assessments.GetLatestCreatedAtAsync(worldId, Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        // …but another host holds the claim.
        _worlds.TryClaimContinuityAuditAsync(
                worldId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await RunOneTickAsync();

        await _auditService.DidNotReceive().RunAssessmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<WorldRole?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EligibleWorldWithAWonClaim_IsAssessed()
    {
        var worldId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        _proposals.ListWorldIdsWithAcceptancesAsync(Arg.Any<CancellationToken>())
            .Returns([worldId]);
        _proposals.GetLatestAcceptanceTimeAsync(worldId, Arg.Any<CancellationToken>())
            .Returns(now.AddHours(-5));
        _assessments.GetLatestCreatedAtAsync(worldId, Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        _worlds.TryClaimContinuityAuditAsync(
                worldId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _auditService.RunAssessmentAsync(worldId, null, null, Arg.Any<CancellationToken>())
            .Returns(AppResult<ContinuityAssessment>.Success(
                new ContinuityAssessment(
                    HasData: true,
                    AssessmentId: Guid.NewGuid(),
                    CreatedAt: now,
                    Model: "nornis-ask",
                    Score: 80,
                    EffectiveScore: 80,
                    HeuristicScore: 75,
                    Findings: [],
                    Penalty: ContinuityPenaltyBreakdown.None(
                        ContinuityAuditService.SeverityScale, ContinuityAuditService.PenaltyCap))));

        await RunOneTickAsync();

        await _auditService.Received(1).RunAssessmentAsync(worldId, null, null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Runs the loop long enough for exactly one tick. The interval is a second rather than
    /// zero because zero now means "disabled".
    /// </summary>
    private async Task RunOneTickAsync()
    {
        var service = CreateService(new ContinuityAuditOptions
        {
            TickIntervalHours = 1.0 / 3600.0,
            QuietPeriodHours = 1.0,
            MinIntervalHours = 20.0,
            ClaimTimeoutHours = 2.0,
        });

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Generous on purpose. The tick itself is one second, but every fixture in this
            // assembly builds its own ASP.NET host, so under a full-suite run the machine is busy
            // enough that a ten-second budget flaked. The deadline exists to stop a hang, not to
            // assert timing — the assertion is that a tick happened at all.
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (Volatile.Read(ref _scopesCreated) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.That(Volatile.Read(ref _scopesCreated), Is.GreaterThan(0), "expected at least one tick");
    }
}
