using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class ReviewProposalTests
{
    private readonly Type _type = typeof(ReviewProposal);

    [Test]
    public void ReviewProposal_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ReviewProposal_Has_ReviewBatchId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("ReviewBatchId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ReviewProposal_Has_ChangeType_Property_Of_Type_ReviewChangeType()
    {
        var property = _type.GetProperty("ChangeType");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ReviewChangeType)));
    }

    [Test]
    public void ReviewProposal_Has_TargetType_Property_Of_Type_ReviewTargetType()
    {
        var property = _type.GetProperty("TargetType");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ReviewTargetType)));
    }

    [Test]
    public void ReviewProposal_Has_TargetId_Property_Of_Type_NullableGuid()
    {
        var property = _type.GetProperty("TargetId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid?)));
    }

    [Test]
    public void ReviewProposal_Has_ProposedValueJson_Property_Of_Type_String()
    {
        var property = _type.GetProperty("ProposedValueJson");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ReviewProposal_Has_Rationale_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Rationale");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ReviewProposal_Has_Confidence_Property_Of_Type_NullableDecimal()
    {
        var property = _type.GetProperty("Confidence");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(decimal?)));
    }

    [Test]
    public void ReviewProposal_Has_Status_Property_Of_Type_ReviewProposalStatus()
    {
        var property = _type.GetProperty("Status");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ReviewProposalStatus)));
    }

    [Test]
    public void ReviewProposal_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void ReviewProposal_Has_ReviewedAt_Property_Of_Type_NullableDateTimeOffset()
    {
        var property = _type.GetProperty("ReviewedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset?)));
    }

    [Test]
    public void ReviewProposal_Has_ReviewedByUserId_Property_Of_Type_NullableGuid()
    {
        var property = _type.GetProperty("ReviewedByUserId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid?)));
    }

    [Test]
    public void ReviewProposal_Has_RowVersion_Property_Of_Type_ByteArray()
    {
        var property = _type.GetProperty("RowVersion");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(byte[])));
    }

    [Test]
    public void ReviewProposal_Has_ReviewBatch_Navigation_Property()
    {
        var property = _type.GetProperty("ReviewBatch");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ReviewBatch)));
    }

    [Test]
    public void ReviewProposal_Has_AppliedToExistingArtifact_Property_Of_Type_NullableBool()
    {
        // Nullable, not bool: rows written before apply-time dedup existed carry no answer,
        // and reprocess must read "unknown" as "not a match" rather than as a real false.
        var property = _type.GetProperty("AppliedToExistingArtifact");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(bool?)));
    }

    [Test]
    public void ReviewProposal_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(15));
    }

    #region Review provenance

    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 8, 3, 14, 30, 0, TimeSpan.Zero);

    private static ReviewProposal Pending() => new() { Id = Guid.NewGuid() };

    [TestCase(nameof(ReviewProposal.Accept), ReviewProposalStatus.Accepted)]
    [TestCase(nameof(ReviewProposal.Reject), ReviewProposalStatus.Rejected)]
    [TestCase(nameof(ReviewProposal.MarkEdited), ReviewProposalStatus.Edited)]
    public void EveryDecision_StampsAllThreeFields(string method, ReviewProposalStatus expected)
    {
        var proposal = Pending();

        _type.GetMethod(method)!.Invoke(proposal, [Reviewer, At]);

        // The three move together or the row is a lie: a resolved proposal with no reviewer
        // is one that decided itself. They used to be assigned separately at eight call
        // sites, which is eight chances to write two of the three.
        Assert.Multiple(() =>
        {
            Assert.That(proposal.Status, Is.EqualTo(expected));
            Assert.That(proposal.ReviewedAt, Is.EqualTo(At));
            Assert.That(proposal.ReviewedByUserId, Is.EqualTo(Reviewer));
        });
    }

    [Test]
    public void ADecision_TakesTheTimeItIsGiven_NotTheClock()
    {
        var proposal = Pending();
        var backdated = DateTimeOffset.UtcNow.AddDays(-30);

        proposal.Accept(Reviewer, backdated);

        // An entity that reads the clock cannot be tested at a chosen moment, and a batch
        // accept would stamp each of its proposals a few milliseconds apart.
        Assert.That(proposal.ReviewedAt, Is.EqualTo(backdated));
    }

    [Test]
    public void NoDecisionMethod_CanStampANonDecisionStatus()
    {
        // The three outcomes are the whole public surface: nothing lets a caller pair a
        // reviewer with Pending, which would read as reviewed-and-still-waiting.
        var stampers = _type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Guid)
                && m.GetParameters()[1].ParameterType == typeof(DateTimeOffset))
            .Select(m => m.Name);

        Assert.That(stampers, Is.EquivalentTo(["Accept", "Reject", "MarkEdited"]));
    }

    #endregion
}
