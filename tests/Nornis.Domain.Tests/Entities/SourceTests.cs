using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class SourceTests
{
    private readonly Type _type = typeof(Source);

    [Test]
    public void Source_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Source_Has_CampaignId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("CampaignId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Source_Has_Type_Property_Of_Type_SourceType()
    {
        var property = _type.GetProperty("Type");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(SourceType)));
    }

    [Test]
    public void Source_Has_Title_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Title");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Source_Has_Body_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Body");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Source_Has_Uri_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Uri");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Source_Has_OccurredAt_Property_Of_Type_NullableDateTimeOffset()
    {
        var property = _type.GetProperty("OccurredAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset?)));
    }

    [Test]
    public void Source_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void Source_Has_CreatedByUserId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("CreatedByUserId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Source_Has_Visibility_Property_Of_Type_VisibilityScope()
    {
        var property = _type.GetProperty("Visibility");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(VisibilityScope)));
    }

    [Test]
    public void Source_Has_ProcessingStatus_Property_Of_Type_SourceProcessingStatus()
    {
        var property = _type.GetProperty("ProcessingStatus");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(SourceProcessingStatus)));
    }

    [Test]
    public void Source_Has_Campaign_Navigation_Property()
    {
        var property = _type.GetProperty("Campaign");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Campaign)));
    }

    [Test]
    public void Source_Has_CreatedByUser_Navigation_Property()
    {
        var property = _type.GetProperty("CreatedByUser");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(User)));
    }

    [Test]
    public void Source_Has_SourceExtractions_Collection_Navigation_Property()
    {
        var property = _type.GetProperty("SourceExtractions");
        Assert.That(property, Is.Not.Null);
        Assert.That(typeof(ICollection<SourceExtraction>).IsAssignableFrom(property!.PropertyType), Is.True,
            $"Expected ICollection<SourceExtraction> but got {property.PropertyType.Name}");
    }

    [Test]
    public void Source_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(14));
    }
}
