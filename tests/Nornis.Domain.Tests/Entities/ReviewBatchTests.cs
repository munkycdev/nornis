using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class ReviewBatchTests
{
    private readonly Type _type = typeof(ReviewBatch);

    [Test]
    public void ReviewBatch_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ReviewBatch_Has_WorldId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("WorldId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ReviewBatch_Has_SourceId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("SourceId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ReviewBatch_Has_Status_Property_Of_Type_ReviewBatchStatus()
    {
        var property = _type.GetProperty("Status");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ReviewBatchStatus)));
    }

    [Test]
    public void ReviewBatch_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void ReviewBatch_Has_CompletedAt_Property_Of_Type_NullableDateTimeOffset()
    {
        var property = _type.GetProperty("CompletedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset?)));
    }

    [Test]
    public void ReviewBatch_Has_World_Navigation_Property()
    {
        var property = _type.GetProperty("World");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(World)));
    }

    [Test]
    public void ReviewBatch_Has_Source_Navigation_Property()
    {
        var property = _type.GetProperty("Source");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Source)));
    }

    [Test]
    public void ReviewBatch_Has_ReviewProposals_Collection_Navigation_Property()
    {
        var property = _type.GetProperty("ReviewProposals");
        Assert.That(property, Is.Not.Null);
        Assert.That(typeof(ICollection<ReviewProposal>).IsAssignableFrom(property!.PropertyType), Is.True,
            $"Expected ICollection<ReviewProposal> but got {property.PropertyType.Name}");
    }

    [Test]
    public void ReviewBatch_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(9));
    }
}
