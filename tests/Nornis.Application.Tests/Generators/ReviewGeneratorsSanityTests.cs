using FsCheck.NUnit;
using Nornis.Application.Tests.Generators;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Generators;

/// <summary>
/// Sanity checks to verify ReviewGenerators produce valid values without exceptions.
/// </summary>
[TestFixture]
public class ReviewGeneratorsSanityTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ReviewArbitraries)],
        MaxTest = 20)]
    public void ReviewScenario_HasConsistentData(ReviewScenario scenario)
    {
        Assert.That(scenario.WorldId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(scenario.Sources, Is.Not.Empty);
        Assert.That(scenario.Batches, Is.Not.Empty);
        Assert.That(scenario.Proposals, Is.Not.Empty);
        Assert.That(scenario.Members.Count, Is.EqualTo(3));
        Assert.That(scenario.GmUserId, Is.Not.EqualTo(scenario.PlayerUserId));
        Assert.That(scenario.GmUserId, Is.Not.EqualTo(scenario.ObserverUserId));

        // All batches reference valid sources
        var sourceIds = scenario.Sources.Select(s => s.Id).ToHashSet();
        foreach (var batch in scenario.Batches)
        {
            Assert.That(sourceIds, Does.Contain(batch.SourceId));
            Assert.That(batch.WorldId, Is.EqualTo(scenario.WorldId));
        }

        // All proposals reference valid batches
        var batchIds = scenario.Batches.Select(b => b.Id).ToHashSet();
        foreach (var proposal in scenario.Proposals)
        {
            Assert.That(batchIds, Does.Contain(proposal.ReviewBatchId));
            Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending));
        }
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ReviewArbitraries)],
        MaxTest = 20)]
    public void ProposalWithContext_HasValidStructure(ProposalWithContext ctx)
    {
        Assert.That(ctx.Proposal.ReviewBatchId, Is.EqualTo(ctx.Batch.Id));
        Assert.That(ctx.Batch.SourceId, Is.EqualTo(ctx.Source.Id));
        Assert.That(ctx.Batch.WorldId, Is.EqualTo(ctx.WorldId));
        Assert.That(ctx.Source.WorldId, Is.EqualTo(ctx.WorldId));
        Assert.That(ctx.Source.CreatedByUserId, Is.EqualTo(ctx.OwnerUserId));
        Assert.That(string.IsNullOrEmpty(ctx.Proposal.ProposedValueJson), Is.False);
    }
}
