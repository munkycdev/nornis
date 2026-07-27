using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Worker.Tests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Nornis.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="ExtractionWorker"/> message handling logic.
/// Tests verify that the worker correctly completes or abandons messages
/// based on extraction outcome, and that structured logging includes
/// required fields (CorrelationId, SourceId, WorldId).
/// </summary>
[TestFixture]
public class ExtractionWorkerTests
{
    private IExtractionService _extractionService = null!;
    private FakeLogger<ExtractionWorker> _logger = null!;
    private TestableExtractionWorker _worker = null!;

    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _extractionService = Substitute.For<IExtractionService>();
        _logger = new FakeLogger<ExtractionWorker>();
        _worker = new TestableExtractionWorker(_extractionService, _logger);
    }

    [Test]
    public async Task ProcessMessage_SuccessOutcome_CompletesMessage()
    {
        // Arrange
        var outcome = ExtractionOutcome.Succeeded(Guid.NewGuid(), 3);
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasCompleted, Is.True);
        Assert.That(context.WasAbandoned, Is.False);
    }

    [Test]
    public async Task ProcessMessage_SkippedOutcome_CompletesMessage()
    {
        // Arrange
        var outcome = ExtractionOutcome.SkippedIdempotent("Source already processed");
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasCompleted, Is.True);
        Assert.That(context.WasAbandoned, Is.False);
    }

    [Test]
    public async Task ProcessMessage_NonTransientFailure_CompletesMessage()
    {
        // Arrange
        var outcome = ExtractionOutcome.NonTransient("SourceNotFound", "Source does not exist");
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasCompleted, Is.True);
        Assert.That(context.WasAbandoned, Is.False);
    }

    [Test]
    public async Task ProcessMessage_TransientFailure_AbandonsMessage()
    {
        // Arrange
        var outcome = ExtractionOutcome.Transient("Timeout", "AI call timed out after 60s");
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasAbandoned, Is.True);
        Assert.That(context.WasCompleted, Is.False);
        Assert.That(context.BackoffApplied, Is.Not.Null,
            "a transient failure must back off before releasing the message, not re-request instantly");
    }

    [Test]
    public async Task ProcessMessage_TransientFailure_BacksOffBeforeReleasingTheMessage()
    {
        // The behaviour this asserts is the whole point of the backoff: answering a throttle with
        // an immediate re-request is what extends the throttle window. An earlier version of this
        // test double abandoned instantly while production backed off, and nothing caught it.
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(ExtractionOutcome.Transient("RateLimited", "429 from the deployment"));

        // First delivery — the shortest real backoff.
        var context = new FakeMessageContext(CreateValidMessageBody()) { DeliveryCount = 1 };

        // Cancelled up front so the assertion is about which delay was chosen, not about
        // spending five seconds proving the clock works.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await _worker.InvokeProcessMessageAsync(context, cts.Token);

        Assert.Multiple(() =>
        {
            Assert.That(context.BackoffApplied, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(context.WasAbandoned, Is.True, "the message must still be released");
        });
    }

    [Test]
    public async Task ProcessMessage_DeserializationFailure_CompletesMessageAndLogsError()
    {
        // Arrange — invalid JSON that won't deserialize to ExtractionMessage
        var context = new FakeMessageContext("this is not valid json {{{");

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasCompleted, Is.True);
        Assert.That(context.WasAbandoned, Is.False);
        Assert.That(_logger.HasLoggedError(), Is.True);
    }

    [Test]
    public async Task ProcessMessage_NullDeserialization_CompletesMessageAndLogsError()
    {
        // Arrange — valid JSON but empty/invalid content (empty GUIDs)
        var context = new FakeMessageContext(
            JsonSerializer.Serialize(new { SourceId = Guid.Empty, WorldId = Guid.Empty }));

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasCompleted, Is.True);
        Assert.That(context.WasAbandoned, Is.False);
        Assert.That(_logger.HasLoggedError(), Is.True);
    }

    [Test]
    public async Task ProcessMessage_UnexpectedException_AbandonsMessage()
    {
        // Arrange — extraction service throws unexpected exception
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected database error"));

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(context.WasAbandoned, Is.True);
        Assert.That(context.WasCompleted, Is.False);
    }

    [Test]
    public async Task ProcessMessage_SuccessOutcome_LogsStructuredFieldsIncludingCorrelationId()
    {
        // Arrange
        var outcome = ExtractionOutcome.Succeeded(Guid.NewGuid(), 5);
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert — verify structured logging includes CorrelationId, SourceId, WorldId
        Assert.That(_logger.HasLoggedContaining("CorrelationId"), Is.True,
            "Log should contain CorrelationId");
        Assert.That(_logger.HasLoggedContaining("SourceId"), Is.True,
            "Log should contain SourceId");
        Assert.That(_logger.HasLoggedContaining("WorldId"), Is.True,
            "Log should contain WorldId");
    }

    [Test]
    public async Task ProcessMessage_TransientFailure_LogsStructuredFieldsIncludingCorrelationId()
    {
        // Arrange
        var outcome = ExtractionOutcome.Transient("TransientError", "Network failure");
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(_logger.HasLoggedContaining("CorrelationId"), Is.True);
        Assert.That(_logger.HasLoggedContaining("SourceId"), Is.True);
        Assert.That(_logger.HasLoggedContaining("WorldId"), Is.True);
    }

    [Test]
    public async Task ProcessMessage_NonTransientFailure_LogsErrorWithStructuredFields()
    {
        // Arrange
        var outcome = ExtractionOutcome.NonTransient("ParseFailure", "AI response malformed");
        _extractionService
            .ProcessExtractionAsync(SourceId, WorldId, Arg.Any<CancellationToken>())
            .Returns(outcome);

        var context = new FakeMessageContext(CreateValidMessageBody());

        // Act
        await _worker.InvokeProcessMessageAsync(context);

        // Assert
        Assert.That(_logger.HasLoggedError(), Is.True);
        Assert.That(_logger.HasLoggedContaining("CorrelationId"), Is.True);
        Assert.That(_logger.HasLoggedContaining("SourceId"), Is.True);
        Assert.That(_logger.HasLoggedContaining("WorldId"), Is.True);
    }

    private string CreateValidMessageBody()
    {
        return JsonSerializer.Serialize(new ExtractionMessage(SourceId, WorldId));
    }
}
