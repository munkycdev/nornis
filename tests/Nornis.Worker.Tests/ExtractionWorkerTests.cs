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
