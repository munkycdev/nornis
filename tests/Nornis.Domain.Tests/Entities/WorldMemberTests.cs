using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class WorldMemberTests
{
    private readonly Type _type = typeof(WorldMember);

    [Test]
    public void WorldMember_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void WorldMember_Has_WorldId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("WorldId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void WorldMember_Has_UserId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("UserId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void WorldMember_Has_Role_Property_Of_Type_WorldRole()
    {
        var property = _type.GetProperty("Role");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(WorldRole)));
    }

    [Test]
    public void WorldMember_Has_DisplayName_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("DisplayName");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void WorldMember_Has_CharacterName_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("CharacterName");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void WorldMember_Has_JoinedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("JoinedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void WorldMember_Has_World_Navigation_Property()
    {
        var property = _type.GetProperty("World");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(World)));
    }

    [Test]
    public void WorldMember_Has_User_Navigation_Property()
    {
        var property = _type.GetProperty("User");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(User)));
    }

    [Test]
    public void WorldMember_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(9));
    }
}
