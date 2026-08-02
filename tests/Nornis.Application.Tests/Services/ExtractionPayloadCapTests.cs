using Nornis.Application.Validation;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The one payload cap. Extraction used to trim proposed values at 50,000 characters while
/// the accept path refused anything over 32,768 — so a payload between the two was stored
/// Pending and failed payload_too_large on every accept, forever. Trimming was itself
/// broken: cutting JSON at a fixed length slices mid-token.
/// </summary>
[TestFixture]
public class ExtractionPayloadCapTests
{
    [Test]
    public void TheAcceptPath_RefusesAnythingOverTheSharedCap()
    {
        var oversized = "{\"name\":\"" + new string('x', ProposalValidator.MaxJsonLength) + "\"}";

        var result = new ProposalValidator()
            .ValidateProposedValue(oversized, ReviewChangeType.CreateArtifact);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("payload_too_large"));
        });
    }

    [Test]
    public void APayloadAtTheCap_IsAccepted()
    {
        // Guards the boundary the two constants used to straddle: exactly at the cap must
        // pass, so extraction and accept agree on the same edge rather than a range.
        var name = new string('x', ProposalValidator.MaxJsonLength - "{\"name\":\"\"}".Length);
        var atCap = "{\"name\":\"" + name + "\"}";
        Assert.That(atCap, Has.Length.EqualTo(ProposalValidator.MaxJsonLength));

        var result = new ProposalValidator()
            .ValidateProposedValue(atCap, ReviewChangeType.CreateArtifact);

        // Schema rules are a separate question — what matters here is that exactly at the
        // cap is not rejected *for size*, so the two ends agree on one edge, not a range.
        Assert.That(result.Error?.Code, Is.Not.EqualTo("payload_too_large"));
    }
}
