using Nornis.Application.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Models;

[TestFixture]
public class ExtractionOutcomeTests
{
    [Test]
    public void Succeeded_SetsTypeToSuccess()
    {
        var batchId = Guid.NewGuid();

        var outcome = ExtractionOutcome.Succeeded(batchId, 5);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(outcome.ReviewBatchId, Is.EqualTo(batchId));
        Assert.That(outcome.ProposalCount, Is.EqualTo(5));
        Assert.That(outcome.ErrorCategory, Is.Null);
        Assert.That(outcome.ErrorMessage, Is.Null);
    }

    [Test]
    public void SkippedIdempotent_SetsTypeToSkipped()
    {
        var outcome = ExtractionOutcome.SkippedIdempotent("Already processed");

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Skipped));
        Assert.That(outcome.ErrorMessage, Is.EqualTo("Already processed"));
        Assert.That(outcome.ErrorCategory, Is.Null);
        Assert.That(outcome.ReviewBatchId, Is.Null);
        Assert.That(outcome.ProposalCount, Is.EqualTo(0));
    }

    [Test]
    public void Transient_SetsTypeToTransientFailure()
    {
        var outcome = ExtractionOutcome.Transient("Timeout", "AI call timed out after 60s");

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.TransientFailure));
        Assert.That(outcome.ErrorCategory, Is.EqualTo("Timeout"));
        Assert.That(outcome.ErrorMessage, Is.EqualTo("AI call timed out after 60s"));
        Assert.That(outcome.ReviewBatchId, Is.Null);
        Assert.That(outcome.ProposalCount, Is.EqualTo(0));
    }

    [Test]
    public void NonTransient_SetsTypeToNonTransientFailure()
    {
        var outcome = ExtractionOutcome.NonTransient("ParseFailure", "Response did not match expected schema");

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.NonTransientFailure));
        Assert.That(outcome.ErrorCategory, Is.EqualTo("ParseFailure"));
        Assert.That(outcome.ErrorMessage, Is.EqualTo("Response did not match expected schema"));
        Assert.That(outcome.ReviewBatchId, Is.Null);
        Assert.That(outcome.ProposalCount, Is.EqualTo(0));
    }
}
