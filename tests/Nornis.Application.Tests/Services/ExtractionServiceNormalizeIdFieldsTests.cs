using System.Text.Json.Nodes;
using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The model occasionally puts artifact NAMES in relationship ID fields; those payloads
/// failed Guid deserialization at accept time during the Symbaroum bulk import.
/// NormalizeIdFields moves such values into the matching Name field at proposal
/// creation, keeping the proposal acceptable.
/// </summary>
[TestFixture]
public class ExtractionServiceNormalizeIdFieldsTests
{
    [Test]
    public void NameInIdField_MovesToNameField()
    {
        var json = """{"artifactAId":"Black Cloak Notes","artifactBId":"7e184448-8ae3-4835-94ff-b358078d0b0c","artifactAName":null,"artifactBName":null,"type":"Describes"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizeIdFields(json, "AddRelationship"))!.AsObject();

        Assert.That(result["artifactAId"]?.GetValue<string?>(), Is.Null);
        Assert.That(result["artifactAName"]!.GetValue<string>(), Is.EqualTo("Black Cloak Notes"));
        Assert.That(result["artifactBId"]!.GetValue<string>(), Is.EqualTo("7e184448-8ae3-4835-94ff-b358078d0b0c"),
            "valid UUIDs must pass through untouched");
    }

    [Test]
    public void NameInIdField_DoesNotOverwriteExistingName()
    {
        var json = """{"artifactAId":"Spider","artifactAName":"Spider Princess","artifactBId":null,"artifactBName":"Karvosti","type":"LocatedIn"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizeIdFields(json, "AddRelationship"))!.AsObject();

        Assert.That(result["artifactAId"]?.GetValue<string?>(), Is.Null);
        Assert.That(result["artifactAName"]!.GetValue<string>(), Is.EqualTo("Spider Princess"),
            "an already-populated name field wins over the misplaced id value");
    }

    [Test]
    public void ValidPayload_IsUnchanged()
    {
        var json = """{"artifactAId":"11111111-1111-1111-1111-111111111111","artifactBId":null,"artifactAName":null,"artifactBName":"Kastor","type":"LocatedIn"}""";

        Assert.That(ExtractionService.NormalizeIdFields(json, "AddRelationship"), Is.EqualTo(json));
    }

    [Test]
    public void NonRelationshipChangeTypes_AreUntouched()
    {
        var json = """{"predicate":"location","value":"Black Harbor","artifactName":"Captain Voss"}""";

        Assert.That(ExtractionService.NormalizeIdFields(json, "AddFact"), Is.EqualTo(json));
    }

    [Test]
    public void MalformedJson_PassesThrough()
    {
        Assert.That(ExtractionService.NormalizeIdFields("not json {", "AddRelationship"), Is.EqualTo("not json {"));
    }
}
