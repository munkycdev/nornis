using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class ReviewQueueFilterTests
{
    private static ReviewProposal Proposal(string changeType, string json) => new(
        Guid.NewGuid(), Guid.NewGuid(), changeType, "Artifact", null, json, null, null, "Pending", DateTimeOffset.UtcNow);

    private static readonly ReviewProposal PartyFact = Proposal("AddFact", """{"predicate":"location","value":"Black Harbor","visibility":"PartyVisible"}""");
    private static readonly ReviewProposal GmFact = Proposal("AddFact", """{"predicate":"secret","value":"the mayor","visibility":"GMOnly"}""");
    private static readonly ReviewProposal GmArtifact = Proposal("CreateArtifact", """{"name":"Captain Voss","type":"Character","visibility":"GMOnly"}""");
    private static readonly ReviewProposal Merge = Proposal("MergeArtifact", """{"sourceArtifactId":"00000000-0000-0000-0000-000000000001"}""");
    private static readonly ReviewProposal Edited = Proposal("UpdateArtifact", """{"Summary":"Edited by the GM","Visibility":"Private"}""");

    private static readonly ReviewProposal[] Queue = [PartyFact, GmFact, GmArtifact, Merge, Edited];

    [Test]
    public void NoFilter_IsTheWholeQueue()
    {
        Assert.That(ReviewQueueFilter.Apply(Queue, null, null), Is.EqualTo(Queue));
    }

    [Test]
    public void Type_KeepsOnlyThatChangeType()
    {
        ReviewProposal[] expected = [PartyFact, GmFact];

        Assert.That(ReviewQueueFilter.Apply(Queue, "AddFact", null), Is.EqualTo(expected));
    }

    [Test]
    public void Visibility_ReadsThePayload_AndHidesProposalsWithout()
    {
        ReviewProposal[] expected = [GmFact, GmArtifact];

        Assert.That(ReviewQueueFilter.Apply(Queue, null, "GMOnly"), Is.EqualTo(expected), "a merge carries no visibility and cannot match one");
    }

    [Test]
    public void BothAxes_Intersect()
    {
        ReviewProposal[] expected = [GmFact];

        Assert.That(ReviewQueueFilter.Apply(Queue, "AddFact", "GMOnly"), Is.EqualTo(expected));
    }

    [Test]
    public void Visibility_IsCaseInsensitiveOnTheKey_ForEditedPayloads()
    {
        Assert.That(ReviewQueueFilter.Visibility(Edited.ProposedValueJson), Is.EqualTo("Private"));
        ReviewProposal[] expected = [Edited];
        Assert.That(ReviewQueueFilter.Apply(Queue, null, "Private"), Is.EqualTo(expected));
    }

    [Test]
    public void Options_ComeFromWhatIsLoaded()
    {
        Assert.Multiple(() =>
        {
            string[] types = ["AddFact", "CreateArtifact", "MergeArtifact", "UpdateArtifact"];
            string[] scopes = ["PartyVisible", "GMOnly", "Private"];
            Assert.That(ReviewQueueFilter.Types(Queue), Is.EqualTo(types));
            Assert.That(ReviewQueueFilter.Visibilities(Queue), Is.EqualTo(scopes),
                "picker order, not first-seen order");
            Assert.That(ReviewQueueFilter.Visibilities([Merge]), Is.Empty);
        });
    }

    [Test]
    public void MalformedPayload_HasNoVisibility()
    {
        Assert.That(ReviewQueueFilter.Visibility("not json"), Is.Null);
        Assert.That(ReviewQueueFilter.Visibility("[1,2]"), Is.Null);
    }
}
