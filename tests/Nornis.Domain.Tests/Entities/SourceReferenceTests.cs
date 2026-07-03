using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class SourceReferenceTests
{
    private readonly Type _type = typeof(SourceReference);

    [Test]
    public void SourceReference_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void SourceReference_Has_SourceId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("SourceId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void SourceReference_Has_TargetType_Property_Of_Type_SourceReferenceTargetType()
    {
        var property = _type.GetProperty("TargetType");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(SourceReferenceTargetType)));
    }

    [Test]
    public void SourceReference_Has_TargetId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("TargetId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void SourceReference_Has_Quote_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Quote");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void SourceReference_Has_Notes_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Notes");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void SourceReference_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void SourceReference_Has_Source_Navigation_Property()
    {
        var property = _type.GetProperty("Source");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Source)));
    }

    [Test]
    public void SourceReference_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(8));
    }
}
