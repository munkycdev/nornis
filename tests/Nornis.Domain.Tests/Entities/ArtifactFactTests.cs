using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class ArtifactFactTests
{
    private readonly Type _type = typeof(ArtifactFact);

    [Test]
    public void ArtifactFact_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ArtifactFact_Has_ArtifactId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("ArtifactId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void ArtifactFact_Has_Predicate_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Predicate");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ArtifactFact_Has_Value_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Value");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void ArtifactFact_Has_Confidence_Property_Of_Type_NullableDecimal()
    {
        var property = _type.GetProperty("Confidence");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(decimal?)));
    }

    [Test]
    public void ArtifactFact_Has_TruthState_Property_Of_Type_TruthState()
    {
        var property = _type.GetProperty("TruthState");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(TruthState)));
    }

    [Test]
    public void ArtifactFact_Has_Visibility_Property_Of_Type_VisibilityScope()
    {
        var property = _type.GetProperty("Visibility");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(VisibilityScope)));
    }

    [Test]
    public void ArtifactFact_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void ArtifactFact_Has_UpdatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("UpdatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void ArtifactFact_Has_RowVersion_Property_Of_Type_ByteArray()
    {
        var property = _type.GetProperty("RowVersion");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(byte[])));
    }

    [Test]
    public void ArtifactFact_Has_Artifact_Navigation_Property()
    {
        var property = _type.GetProperty("Artifact");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Artifact)));
    }

    [Test]
    public void ArtifactFact_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(13));
    }
}
