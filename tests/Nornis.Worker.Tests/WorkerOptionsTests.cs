using Microsoft.Extensions.Configuration;
using Nornis.Worker.Configuration;
using NUnit.Framework;

namespace Nornis.Worker.Tests;

/// <summary>
/// The two queue processors share one options object but not one workload: extraction is a
/// bounded handful of AI calls, library indexing is a whole PDF's worth of serial embedding
/// round trips. They therefore need separate lock-renewal ceilings — if indexing's lock lapses
/// mid-run, Service Bus redelivers and a second indexing run starts alongside the first,
/// re-buying every embedding for that document.
/// </summary>
[TestFixture]
public class WorkerOptionsTests
{
    [Test]
    public void LibraryLockRenewal_DefaultsLongerThanExtraction()
    {
        var options = new WorkerOptions();

        Assert.That(
            options.LibraryMaxAutoLockRenewalDuration,
            Is.GreaterThan(options.MaxAutoLockRenewalDuration),
            "indexing runs far longer than extraction; an equal ceiling reintroduces duplicate delivery");
    }

    [Test]
    public void LibraryLockRenewal_DefaultLeavesRoomForABookSizedDocument()
    {
        // Not an arbitrary number: a large sourcebook is dozens of sequential embedding calls,
        // and the ceiling costs nothing when runs finish early because the lock is released on
        // completion. Pinned so a future trim back toward five minutes is a deliberate choice.
        Assert.That(
            new WorkerOptions().LibraryMaxAutoLockRenewalDuration,
            Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void BothRenewalDurations_BindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceBus:MaxAutoLockRenewalDuration"] = "00:05:00",
                ["ServiceBus:LibraryMaxAutoLockRenewalDuration"] = "00:45:00",
            })
            .Build();

        var options = configuration.GetSection("ServiceBus").Get<WorkerOptions>();

        Assert.Multiple(() =>
        {
            Assert.That(options!.MaxAutoLockRenewalDuration, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(options.LibraryMaxAutoLockRenewalDuration, Is.EqualTo(TimeSpan.FromMinutes(45)));
        });
    }

    [Test]
    public void ShippedAppSettings_GiveIndexingMoreHeadroomThanExtraction()
    {
        // Guards the deployed configuration, not just the defaults — appsettings.json overrides
        // both values, so a correct default alone would not protect production.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var options = configuration.GetSection("ServiceBus").Get<WorkerOptions>();

        Assert.That(
            options!.LibraryMaxAutoLockRenewalDuration,
            Is.GreaterThan(options.MaxAutoLockRenewalDuration));
    }
}
