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
    public void ReviewProposal_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(14));
    }
}
