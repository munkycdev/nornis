using System.Reflection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class AiUsageRecordTests
{
    private readonly Type _type = typeof(AiUsageRecord);

    [Test]
    public void AiUsageRecord_Has_Id_Property_Of_Type_Guid()
    {
        var property = _type.GetProperty("Id");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid)));
    }

    [Test]
    public void AiUsageRecord_Has_WorldId_Property_Of_Type_NullableGuid()
    {
        var property = _type.GetProperty("WorldId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid?)));
    }

    [Test]
    public void AiUsageRecord_Has_UserId_Property_Of_Type_NullableGuid()
    {
        var property = _type.GetProperty("UserId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid?)));
    }

    [Test]
    public void AiUsageRecord_Has_OperationType_Property_Of_Type_AiOperationType()
    {
        var property = _type.GetProperty("OperationType");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(AiOperationType)));
    }

    [Test]
    public void AiUsageRecord_Has_Model_Property_Of_Type_String()
    {
        var property = _type.GetProperty("Model");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void AiUsageRecord_Has_InputTokens_Property_Of_Type_Int()
    {
        var property = _type.GetProperty("InputTokens");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void AiUsageRecord_Has_OutputTokens_Property_Of_Type_Int()
    {
        var property = _type.GetProperty("OutputTokens");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void AiUsageRecord_Has_TotalTokens_Property_Of_Type_Int()
    {
        var property = _type.GetProperty("TotalTokens");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void AiUsageRecord_Has_EstimatedCostUsd_Property_Of_Type_Decimal()
    {
        var property = _type.GetProperty("EstimatedCostUsd");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(decimal)));
    }

    [Test]
    public void AiUsageRecord_Has_SourceId_Property_Of_Type_NullableGuid()
    {
        var property = _type.GetProperty("SourceId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid?)));
    }

    [Test]
    public void AiUsageRecord_Has_ReviewBatchId_Property_Of_Type_NullableGuid()
    {
        var property = _type.GetProperty("ReviewBatchId");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(Guid?)));
    }

    [Test]
    public void AiUsageRecord_Has_DurationMs_Property_Of_Type_Int()
    {
        var property = _type.GetProperty("DurationMs");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void AiUsageRecord_Has_Succeeded_Property_Of_Type_Bool()
    {
        var property = _type.GetProperty("Succeeded");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(bool)));
    }

    [Test]
    public void AiUsageRecord_Has_ErrorCode_Property_Of_Type_NullableString()
    {
        var property = _type.GetProperty("ErrorCode");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void AiUsageRecord_Has_CreatedAt_Property_Of_Type_DateTimeOffset()
    {
        var property = _type.GetProperty("CreatedAt");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(DateTimeOffset)));
    }

    [Test]
    public void AiUsageRecord_Has_Expected_Property_Count()
    {
        var properties = _type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.That(properties, Has.Length.EqualTo(15));
    }
}
