using System.Text.Json;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Worker.Tests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Nornis.Worker.Tests;

/// <summary>
/// The last delivery is where a transient failure used to wedge a source at Queued: the abandon
/// dead-letters the message, and nothing ever moves the source again. On 2026-11-06 a Ruins of
/// Symbaroum note timed out five times and showed "extracting" for seventeen hours.
/// </summary>
[TestFixture]
public class LastDeliveryTests
{
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid WorldId = Guid.NewGuid();

    private IExtractionService _extraction = null!;
    private FakeLogger<ExtractionWorker> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _extraction = Substitute.For<IExtractionService>();
        _logger = new FakeLogger<ExtractionWorker>();
    }

    [TestCase(1, false)]
    [TestCase(4, false)]
    [TestCase(5, true)]
    [TestCase(6, true)]
    public void IsLast_IsTheDeliveryThatDeadLetters(long deliveryCount, bool last)
    {
        Assert.That(LastDelivery.IsLast(deliveryCount), Is.EqualTo(last));
    }

    [Test]
    public async Task TransientOnTheLastDelivery_FailsTheSourceWithItsReason_AndStillReleasesTheMessage()
    {
        _extraction.ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(ExtractionOutcome.Transient("Timeout", "AI call timed out after 180 seconds"));
        var context = new FakeMessageContext(Body()) { DeliveryCount = RedeliveryBackoff.QueueMaxDeliveryCount };

        await new TestableExtractionWorker(_extraction, _logger).InvokeProcessMessageAsync(context);

        await _extraction.Received(1).GiveUpAsync(SourceId, "Timeout", Arg.Any<CancellationToken>());
        Assert.That(context.WasAbandoned, Is.True, "the message still dead-letters, where the alert counts it");
    }

    [Test]
    public async Task TransientBeforeTheLastDelivery_LeavesTheSourceForTheRedelivery()
    {
        _extraction.ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(ExtractionOutcome.Transient("Timeout", "AI call timed out after 180 seconds"));
        var context = new FakeMessageContext(Body()) { DeliveryCount = 1 };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await new TestableExtractionWorker(_extraction, _logger).InvokeProcessMessageAsync(context, cts.Token);

        await _extraction.DidNotReceiveWithAnyArgs().GiveUpAsync(default, default, default);
    }

    [Test]
    public async Task UnexpectedExceptionOnTheLastDelivery_FailsTheSource()
    {
        _extraction.ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database went away"));
        var context = new FakeMessageContext(Body()) { DeliveryCount = RedeliveryBackoff.QueueMaxDeliveryCount };

        await new TestableExtractionWorker(_extraction, _logger).InvokeProcessMessageAsync(context);

        await _extraction.Received(1).GiveUpAsync(SourceId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.That(context.WasAbandoned, Is.True);
    }

    [TestCase(ExtractionKind.RelationshipBackfill)]
    [TestCase(ExtractionKind.SummaryRefresh)]
    public async Task OtherKinds_NeverTouchTheSource(ExtractionKind kind)
    {
        // A backfill runs over a Processed source; failing it would undo a finished extraction.
        var message = new ExtractionMessage(SourceId, WorldId) { Kind = kind };

        await LastDelivery.GiveUpIfLastAsync(_extraction, message, RedeliveryBackoff.QueueMaxDeliveryCount, "Timeout", _logger);

        await _extraction.DidNotReceiveWithAnyArgs().GiveUpAsync(default, default, default);
    }

    [Test]
    public async Task AFailedWrite_IsLoggedAndSwallowed_SoTheMessageIsStillReleased()
    {
        _extraction.GiveUpAsync(SourceId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database went away"));
        var message = new ExtractionMessage(SourceId, WorldId);

        await Assert.DoesNotThrowAsync(() => LastDelivery.GiveUpIfLastAsync(
            _extraction, message, RedeliveryBackoff.QueueMaxDeliveryCount, "Timeout", _logger));
        Assert.That(_logger.HasLoggedError(), Is.True);
    }

    private static string Body() => JsonSerializer.Serialize(new ExtractionMessage(SourceId, WorldId));
}
