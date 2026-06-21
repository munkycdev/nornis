using System.Reflection;
using NUnit.Framework;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class ArtifactTests
{
    private readonly Type _type = typeof(Artifact);

    [Test]
    public void Artifact_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Artifact_Has_CampaignId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("CampaignId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Artifact_Has_Type_Property_Of_Type_ArtifactType()
    {
        var property = _type.GetProperty("Type");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ArtifactType)));
    }

    [Test]
    public void Artifact_Has_Name_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Name");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Artifact_Has_Summary_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Summary");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Artifact_Has_Visibility_Property_Of_Type_VisibilityScope()
    {
        var property = _type.GetProperty("Visibility");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(VisibilityScope)));
    }

    [Test]
    public void Artifact_Has_Confidence_Property_Of_Type_NullableDecimal()
    {
        var property = _type.GetProperty("Confidence");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(decimal?)));
    }

    [Test]
    public void Artifact_Has_Status_Property_Of_Type_ArtifactStatus()
    {
        var property = _type.GetProperty("Status");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(ArtifactStatus)));
    }

    [Test]
    public void Artifact_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void Artifact_Has_UpdatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("UpdatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void Artifact_Has_RowVersion_Property_Of_Type_ByteArray()
    {
        var property = _type.GetProperty("RowVersion");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(byte[])));
    }

    [Test]
    public void Artifact_Has_Campaign_Navigation_Property()
    {
        var property = _type.GetProperty("Campaign");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Campaign)));
    }

    [Test]
    public void Artifact_Has_ArtifactFacts_Collection_Navigation_Property()
    {
        var property = _type.GetProperty("ArtifactFacts");
        Assert.That(property, Is.Not.Null);
        Assert.That(typeof(ICollection<ArtifactFact>).IsAssignableFrom(property!.PropertyType), Is.True,
            $"Expected ICollection<ArtifactFact> but got {property.PropertyType.Name}");
    }

    [Test]
    public void Artifact_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(13));
    }
}
