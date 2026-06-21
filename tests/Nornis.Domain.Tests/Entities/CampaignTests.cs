using System.Reflection;
using NUnit.Framework;
using Nornis.Domain.Entities;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class CampaignTests
{
    private readonly Type _type = typeof(Campaign);

    [Test]
    public void Campaign_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Campaign_Has_Name_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Name");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Campaign_Has_Description_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("Description");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Campaign_Has_GameSystem_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("GameSystem");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Campaign_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void Campaign_Has_UpdatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("UpdatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void Campaign_Has_CreatedByUserId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("CreatedByUserId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void Campaign_Has_RowVersion_Property_Of_Type_ByteArray()
    {
        var property = _type.GetProperty("RowVersion");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(byte[])));
    }

    [Test]
    public void Campaign_Has_CreatedByUser_Navigation_Property()
    {
        var property = _type.GetProperty("CreatedByUser");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(User)));
    }

    [Test]
    public void Campaign_Has_CampaignMembers_Collection_Navigation_Property()
    {
        var property = _type.GetProperty("CampaignMembers");
        Assert.That(property, Is.Not.Null);
        Assert.That(typeof(ICollection<CampaignMember>).IsAssignableFrom(property!.PropertyType), Is.True,
            $"Expected ICollection<CampaignMember> but got {property.PropertyType.Name}");
    }

    [Test]
    public void Campaign_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(10));
    }
}
