using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// The contract between the convergence gauge and the reveal dialog: what the gauge suggests
/// arrives ticked, nothing else does, and an id that has stopped being GM-only is dropped
/// rather than carried into a request the reveal would reject.
/// </summary>
[TestFixture]
public class RevealPreselectionTests
{
    private static readonly Guid ArtifactId = Guid.NewGuid();
    private static readonly Guid SecretFactId = Guid.NewGuid();
    private static readonly Guid OtherSecretFactId = Guid.NewGuid();
    private static readonly Guid KnownFactId = Guid.NewGuid();
    private static readonly Guid SecretRelationshipId = Guid.NewGuid();

    [Test]
    public void TheSuggestedFact_IsSelected()
    {
        var match = RevealPreselection.Match(Detail(), [SecretFactId]);

        Assert.Multiple(() =>
        {
            Assert.That(match.FactIds, Is.EquivalentTo([SecretFactId]));
            Assert.That(match.Artifact, Is.False);
            Assert.That(match.RelationshipIds, Is.Empty);
        });
    }

    [Test]
    public void ItsNeighbours_AreNot()
    {
        // A suggestion that pre-selects what it did not suggest is making a choice the GM did not.
        var match = RevealPreselection.Match(Detail(), [SecretFactId]);

        Assert.That(match.FactIds, Does.Not.Contain(OtherSecretFactId));
    }

    [Test]
    public void AFactThatIsNoLongerGmOnly_IsDropped()
    {
        // The gauge is a snapshot; this is what a race between reading it and acting looks like.
        var match = RevealPreselection.Match(Detail(), [KnownFactId]);

        Assert.Multiple(() =>
        {
            Assert.That(match.FactIds, Is.Empty);
            Assert.That(match.Artifact, Is.False);
            Assert.That(match.RelationshipIds, Is.Empty);
        });
    }

    [Test]
    public void ASuggestedRelationship_IsSelected()
    {
        var match = RevealPreselection.Match(Detail(), [SecretRelationshipId]);

        Assert.That(match.RelationshipIds, Is.EquivalentTo([SecretRelationshipId]));
    }

    [Test]
    public void TheArtifactItself_IsSelectedOnlyWhenItIsGmOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RevealPreselection.Match(Detail(artifactVisibility: "GMOnly"), [ArtifactId]).Artifact,
                Is.True);
            Assert.That(RevealPreselection.Match(Detail(artifactVisibility: "PartyVisible"), [ArtifactId]).Artifact,
                Is.False, "a party-visible artifact has nothing left to reveal");
        });
    }

    [Test]
    public void NoSuggestion_SelectsNothing()
    {
        // The artifact-detail entry point passes nothing and must still open on a blank checklist.
        var match = RevealPreselection.Match(Detail(), null);

        Assert.Multiple(() =>
        {
            Assert.That(match.Artifact, Is.False);
            Assert.That(match.FactIds, Is.Empty);
            Assert.That(match.RelationshipIds, Is.Empty);
        });
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ArtifactDetailDto Detail(string artifactVisibility = "PartyVisible") => new(
        ArtifactId,
        Guid.NewGuid(),
        "Character",
        "Captain Voss",
        null,
        "Active",
        artifactVisibility,
        null,
        Now.AddDays(-100),
        Now,
        [
            new ArtifactFactDto(SecretFactId, ArtifactId, "true allegiance", "sworn to the cult", null, "Confirmed", "GMOnly", Now, Now),
            new ArtifactFactDto(OtherSecretFactId, ArtifactId, "hidden ledger", "beneath the floor", null, "Confirmed", "GMOnly", Now, Now),
            new ArtifactFactDto(KnownFactId, ArtifactId, "rank", "harbour captain", null, "Confirmed", "PartyVisible", Now, Now)
        ],
        [
            new ArtifactRelationshipDto(SecretRelationshipId, ArtifactId, Guid.NewGuid(), "MemberOf", null, null, "Confirmed", "GMOnly")
        ],
        [],
        []);
}
