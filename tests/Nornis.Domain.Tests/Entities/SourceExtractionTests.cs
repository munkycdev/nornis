using System.Reflection;
using NUnit.Framework;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class SourceExtractionTests
{
    private readonly Type _type = typeof(SourceExtraction);

    [Test]
    public void SourceExtraction_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void SourceExtraction_Has_SourceId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("SourceId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void SourceExtraction_Has_ExtractionType_Property_Of_Type_SourceExtractionType()
    {
        var property = _type.GetProperty("ExtractionType");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(SourceExtractionType)));
    }

    [Test]
    public void SourceExtraction_Has_Text_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Text");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void SourceExtraction_Has_Confidence_Property_Of_Type_NullableDecimal()
    {
        var property = _type.GetProperty("Confidence");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(decimal?)));
    }

    [Test]
    public void SourceExtraction_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void SourceExtraction_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(6));
    }
}
