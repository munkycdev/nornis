using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The kill switch's read path. Two properties matter more than the happy case: it must not
/// query per call, and it must never turn an unreadable database into a total AI outage.
/// </summary>
[TestFixture]
public class AiPauseGateTests
{
    private CountingFlagRepository _flags = null!;
    private FakeTimeProvider _time = null!;
    private AiPauseGate _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _flags = new CountingFlagRepository();
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-02T12:00:00Z"));
        _sut = new AiPauseGate(_flags, NullLogger<AiPauseGate>.Instance, _time);
    }

    [Test]
    public async Task NoFlagRow_ReadsAsRunning()
    {
        var state = await _sut.GetAsync(CancellationToken.None);

        Assert.That(state.IsPaused, Is.False);
    }

    [Test]
    public async Task EnabledFlag_PausesAndCarriesTheReason()
    {
        _flags.Flag = new OperationalFlag
        {
            Name = OperationalFlagNames.AiPaused,
            Enabled = true,
            Reason = "Provider incident"
        };

        var state = await _sut.GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsPaused, Is.True);
            Assert.That(state.Reason, Is.EqualTo("Provider incident"));
        });
    }

    [Test]
    public async Task RepeatedReads_QueryOncePerCacheWindow()
    {
        for (var i = 0; i < 20; i++)
        {
            await _sut.GetAsync(CancellationToken.None);
        }

        // Every paid AI dispatch calls this. Without the cache that is a database round trip
        // per extraction, per Ask, per indexing batch.
        Assert.That(_flags.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task AfterTheCacheExpires_TheFlagIsReadAgain()
    {
        await _sut.GetAsync(CancellationToken.None);
        _flags.Flag = new OperationalFlag { Name = OperationalFlagNames.AiPaused, Enabled = true };

        _time.Advance(TimeSpan.FromSeconds(61));
        var state = await _sut.GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsPaused, Is.True, "a flip must take effect without a redeploy");
            Assert.That(_flags.Reads, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task WhenTheFlagCannotBeRead_ItFailsOpen()
    {
        _flags.Throw = new InvalidOperationException("database is down");

        var state = await _sut.GetAsync(CancellationToken.None);

        // Failing closed would turn a database blip into the outage this switch exists to
        // end — and a phantom pause is one nobody can turn off, because turning it off also
        // needs the database.
        Assert.That(state.IsPaused, Is.False);
    }

    [Test]
    public async Task ConcurrentColdReads_QueryOnce()
    {
        var reads = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => _sut.GetAsync(CancellationToken.None)));

        Assert.Multiple(() =>
        {
            Assert.That(reads.Select(r => r.IsPaused), Is.All.False);
            Assert.That(_flags.Reads, Is.EqualTo(1), "a cold cache must not let a burst stampede the database");
        });
    }

    /// <summary>
    /// Ten lines rather than a package reference: TimeProvider is only used by the gate, and
    /// only GetUtcNow needs controlling.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class CountingFlagRepository : IOperationalFlagRepository
    {
        public OperationalFlag? Flag { get; set; }
        public Exception? Throw { get; set; }
        public int Reads { get; private set; }

        public Task<OperationalFlag?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Throw is not null ? Task.FromException<OperationalFlag?>(Throw) : Task.FromResult(Flag);
        }
    }
}
