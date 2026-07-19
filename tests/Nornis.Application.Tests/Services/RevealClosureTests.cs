using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class RevealClosureTests
{
    private static readonly Guid ArtifactA = Guid.NewGuid();
    private static readonly Guid ArtifactB = Guid.NewGuid();
    private static readonly Guid ArtifactC = Guid.NewGuid();

    [Test]
    public void EmptyReveal_HasNoMissingDependencies()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            [], [], [], new Dictionary<Guid, VisibilityScope>());

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void FactWhoseParentIsAlreadyPartyVisible_IsClosed()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [],
            revealFactParentArtifactIds: [ArtifactA],
            revealRelationshipEndpoints: [],
            new Dictionary<Guid, VisibilityScope> { [ArtifactA] = VisibilityScope.PartyVisible });

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void FactWhoseParentIsGmOnlyAndNotRevealed_ReportsTheParent()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [],
            revealFactParentArtifactIds: [ArtifactA],
            revealRelationshipEndpoints: [],
            new Dictionary<Guid, VisibilityScope> { [ArtifactA] = VisibilityScope.GMOnly });

        Assert.That(missing, Is.EqualTo(new[] { ArtifactA }));
    }

    [Test]
    public void FactWhoseParentIsInTheRevealSet_IsClosed()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [ArtifactA],
            revealFactParentArtifactIds: [ArtifactA],
            revealRelationshipEndpoints: [],
            new Dictionary<Guid, VisibilityScope> { [ArtifactA] = VisibilityScope.GMOnly });

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void RelationshipWithGmOnlyEndpointNotRevealed_ReportsThatEndpoint()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [],
            revealFactParentArtifactIds: [],
            revealRelationshipEndpoints: [(ArtifactA, ArtifactB)],
            new Dictionary<Guid, VisibilityScope>
            {
                [ArtifactA] = VisibilityScope.PartyVisible,
                [ArtifactB] = VisibilityScope.GMOnly
            });

        Assert.That(missing, Is.EqualTo(new[] { ArtifactB }));
    }

    [Test]
    public void RelationshipWithBothEndpointsCovered_IsClosed()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [ArtifactB],
            revealFactParentArtifactIds: [],
            revealRelationshipEndpoints: [(ArtifactA, ArtifactB)],
            new Dictionary<Guid, VisibilityScope>
            {
                [ArtifactA] = VisibilityScope.PartyVisible,
                [ArtifactB] = VisibilityScope.GMOnly
            });

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void MultipleFactsWithTheSameMissingParent_ReportItOnce()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [],
            revealFactParentArtifactIds: [ArtifactA, ArtifactA, ArtifactA],
            revealRelationshipEndpoints: [],
            new Dictionary<Guid, VisibilityScope> { [ArtifactA] = VisibilityScope.GMOnly });

        Assert.That(missing, Is.EqualTo(new[] { ArtifactA }));
    }

    [Test]
    public void Missing_IsOrdered_FactParentsThenRelationshipEndpoints()
    {
        var missing = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [],
            revealFactParentArtifactIds: [ArtifactA],
            revealRelationshipEndpoints: [(ArtifactB, ArtifactC)],
            new Dictionary<Guid, VisibilityScope>
            {
                [ArtifactA] = VisibilityScope.GMOnly,
                [ArtifactB] = VisibilityScope.GMOnly,
                [ArtifactC] = VisibilityScope.GMOnly
            });

        Assert.That(missing, Is.EqualTo(new[] { ArtifactA, ArtifactB, ArtifactC }));
    }
}
