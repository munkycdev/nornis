using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Nornis.Worker.Tests;

[TestFixture]
public class HeartbeatWorkerTests
{
    private IWorkerHeartbeatRepository _repository = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IWorkerHeartbeatRepository>();
        _services = new ServiceCollection()
            .AddScoped(_ => _repository)
            .BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
    }

    /// <summary>
    /// Exposes the loop and a single beat, the way ExtractionWorkerTests exposes message
    /// handling — the hosted-service lifecycle is the framework's business, not this
    /// test's, and under .NET 10 StartAsync no longer runs ExecuteAsync anyway.
    /// </summary>
    private sealed class TestableHeartbeatWorker : HeartbeatWorker
    {
        public TestableHeartbeatWorker(IServiceScopeFactory scopeFactory)
            : base(scopeFactory, NullLogger<HeartbeatWorker>.Instance)
        {
        }

        public Task RunLoopAsync(CancellationToken ct) => ExecuteAsync(ct);

        public Task BeatOnceAsync(CancellationToken ct) => BeatAsync(ct);
    }

    private TestableHeartbeatWorker CreateWorker() =>
        new(_services.GetRequiredService<IServiceScopeFactory>());

    [Test]
    public async Task Beat_WritesUnderTheNameTheHealthCheckReads()
    {
        await CreateWorker().BeatOnceAsync(CancellationToken.None);

        // The two sides agree by construction rather than by two copies of a literal — a
        // mismatch would leave the check reading a row nobody ever writes, which reads as
        // a permanently dead worker.
        await _repository.Received(1).BeatAsync(
            WorkerHeartbeatHealthCheck.WorkerName,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void Beat_WhenTheWriteFails_DoesNotThrow()
    {
        _repository.BeatAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unreachable"));

        // A database outage must not take extraction down with it — the sql check on the
        // same status page is what reports that failure.
        Assert.DoesNotThrowAsync(() => CreateWorker().BeatOnceAsync(CancellationToken.None));
    }

    [Test]
    public async Task Loop_BeatsBeforeItWaits()
    {
        // Cancelling from inside the first beat means the loop can only reach its delay
        // after having already written — no timers, no sleeping, no flake.
        using var cts = new CancellationTokenSource();
        _repository
            .When(r => r.BeatAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()))
            .Do(_ => cts.Cancel());

        await CreateWorker().RunLoopAsync(cts.Token);

        // A worker that waited out its interval first would look dead for a minute after
        // every deploy — exactly when someone is watching the status page.
        await _repository.Received(1).BeatAsync(
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void BeatInterval_StaysWellInsideTheDegradedThreshold()
    {
        // Pairing, not numbers: lengthen the interval past the threshold and a perfectly
        // healthy worker starts flapping amber between beats.
        Assert.That(
            HeartbeatWorker.Interval * 2,
            Is.LessThanOrEqualTo(WorkerHeartbeatHealthCheck.DegradedAfter));
    }
}
