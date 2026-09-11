using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Api.BackgroundServices;
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.BackgroundServices;

[TestFixture]
public class SlugBackfillBackgroundServiceTests
{
    private ISlugBackfiller _backfiller = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _backfiller = Substitute.For<ISlugBackfiller>();
        var services = new ServiceCollection();
        services.AddSingleton(_backfiller);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private SlugBackfillBackgroundService CreateService() =>
        new(_scopeFactory, NullLogger<SlugBackfillBackgroundService>.Instance, TimeSpan.Zero);

    [Test]
    public async Task RunsTheBackfillOnce_ThenExits()
    {
        _backfiller.BackfillAsync(Arg.Any<CancellationToken>()).Returns(3);
        var service = CreateService();

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        await _backfiller.Received(1).BackfillAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AFailedBackfill_DoesNotTakeTheHostDown()
    {
        _backfiller.BackfillAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("index race"));
        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        Assert.DoesNotThrowAsync(() => service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task CancellationDuringTheDelay_SkipsTheWork()
    {
        var service = new SlugBackfillBackgroundService(
            _scopeFactory, NullLogger<SlugBackfillBackgroundService>.Instance, TimeSpan.FromMinutes(5));
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        await _backfiller.DidNotReceive().BackfillAsync(Arg.Any<CancellationToken>());
    }
}
