using System.Reflection;
using Nornis.Domain.Entities;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

/// <summary>
/// Cross-cutting structural tests that verify all entities follow domain conventions.
/// </summary>
[TestFixture]
public class EntityStructureTests
{
    private static readonly Type[] AllEntityTypes =
    [
        typeof(User),
        typeof(World),
        typeof(WorldMember),
        typeof(Source),
        typeof(SourceExtraction),
        typeof(Artifact),
        typeof(ArtifactFact),
        typeof(ArtifactRelationship),
        typeof(SourceReference),
        typeof(ReviewBatch),
        typeof(ReviewProposal),
        typeof(AiUsageRecord)
    ];

    [TestCaseSource(nameof(AllEntityTypes))]
    public void Entity_Uses_DateTimeOffset_For_All_Timestamp_Properties(Type entityType)
    {
        var timestampProperties = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("At") || p.Name.Contains("Timestamp"));

        Assert.That(timestampProperties, Is.Not.Empty,
            $"{entityType.Name} should have at least one timestamp property");

        foreach (var property in timestampProperties)
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Assert.That(propertyType, Is.EqualTo(typeof(DateTimeOffset)),
                $"{entityType.Name}.{property.Name} should use DateTimeOffset, but uses {property.PropertyType.Name}");
        }
    }

    [Test]
    public void All_Entities_Exist_In_Nornis_Domain_Entities_Namespace()
    {
        foreach (var entityType in AllEntityTypes)
        {
            Assert.That(entityType.Namespace, Is.EqualTo("Nornis.Domain.Entities"),
                $"{entityType.Name} should be in the Nornis.Domain.Entities namespace");
        }
    }

    [Test]
    public void Nullable_Reference_Types_Are_Enabled_For_Domain_Assembly()
    {
        // When nullable reference types are enabled, the assembly gets a
        // NullableContextAttribute with Flag = 1 (enable) or 2 (annotations)
        var assembly = typeof(User).Assembly;
        var nullableContextAttribute = assembly.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");

        // If the assembly-level attribute exists, nullable is enabled
        // Otherwise check at module level
        if (nullableContextAttribute is not null)
        {
            var flag = (byte)nullableContextAttribute.ConstructorArguments[0].Value!;
            Assert.That(flag, Is.EqualTo((byte)1).Or.EqualTo((byte)2),
                "NullableContextAttribute flag should indicate nullable is enabled");
        }
        else
        {
            // Check individual entity types for NullableContextAttribute
            foreach (var entityType in AllEntityTypes)
            {
                var typeNullableContext = entityType.CustomAttributes
                    .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");

                Assert.That(typeNullableContext, Is.Not.Null,
                    $"{entityType.Name} should have NullableContextAttribute (nullable reference types enabled)");
            }
        }
    }

    [Test]
    public void All_Entities_Have_Public_Parameterless_Constructor()
    {
        foreach (var entityType in AllEntityTypes)
        {
            var constructor = entityType.GetConstructor(Type.EmptyTypes);
            Assert.That(constructor, Is.Not.Null,
                $"{entityType.Name} should have a public parameterless constructor");
        }
    }

    [Test]
    public void All_Entities_Have_Id_Property_Of_Type_Guid()
    {
        foreach (var entityType in AllEntityTypes)
        {
            var idProperty = entityType.GetProperty("Id");
            Assert.That(idProperty, Is.Not.Null,
                $"{entityType.Name} should have an Id property");
            Assert.That(idProperty!.PropertyType, Is.EqualTo(typeof(Guid)),
                $"{entityType.Name}.Id should be of type Guid");
        }
    }

    [TestCaseSource(nameof(AllEntityTypes))]
    public void Entity_Properties_Have_Public_Getters_And_Setters(Type entityType)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            Assert.That(property.GetMethod, Is.Not.Null,
                $"{entityType.Name}.{property.Name} should have a public getter");
            Assert.That(property.SetMethod, Is.Not.Null,
                $"{entityType.Name}.{property.Name} should have a public setter");
        }
    }

    [Test]
    public void Domain_Assembly_Contains_Exactly_21_Entity_Classes()
    {
        var entityTypes = typeof(User).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Nornis.Domain.Entities" && t.IsClass && !t.IsAbstract)
            .ToList();

        // 12 original + HealthAssessment + ContinuityFinding (AI-assessed Continuity
        // Health) + Campaign/Character/CampaignCharacter (worlds-and-campaigns)
        // + SourceAttachment (handwritten notes) + LibraryDocument/LibraryChunk (Library)
        // + MapPlacemark (map sources).
        Assert.That(entityTypes, Has.Count.EqualTo(21));
    }
}
