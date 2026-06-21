using System.Reflection;
using NUnit.Framework;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class CampaignMemberTests
{
    private readonly Type _type = typeof(CampaignMember);

    [Test]
    public void CampaignMember_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void CampaignMember_Has_CampaignId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("CampaignId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void CampaignMember_Has_UserId_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("UserId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void CampaignMember_Has_Role_Property_Of_Type_CampaignRole()
    {
        var property = _type.GetProperty("Role");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(CampaignRole)));
    }

    [Test]
    public void CampaignMember_Has_DisplayName_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("DisplayName");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void CampaignMember_Has_CharacterName_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("CharacterName");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void CampaignMember_Has_JoinedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("JoinedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void CampaignMember_Has_Campaign_Navigation_Property()
    {
        var property = _type.GetProperty("Campaign");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Campaign)));
    }

    [Test]
    public void CampaignMember_Has_User_Navigation_Property()
    {
        var property = _type.GetProperty("User");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(User)));
    }

    [Test]
    public void CampaignMember_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(9));
    }
}
