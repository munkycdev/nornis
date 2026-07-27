using System.Text.Json.Nodes;
using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Payloads are tidied on the way in so a sloppy generation never becomes an unacceptable
/// proposal: quoted numbers become numbers, and the name fields used for matching get their
/// whitespace collapsed. The applicator and validator separately tolerate quoted numbers for
/// rows written before this existed.
/// </summary>
[TestFixture]
public class ExtractionServicePayloadNormalizationTests
{
    #region Numeric fields

    [Test]
    public void QuotedConfidence_BecomesANumber()
    {
        var json = """{"name":"Captain Voss","type":"Character","confidence":"0.99"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["confidence"]!.GetValue<decimal>(), Is.EqualTo(0.99m));
        Assert.That(result["name"]!.GetValue<string>(), Is.EqualTo("Captain Voss"),
            "everything else passes through untouched");
    }

    [Test]
    public void QuotedConfidence_IsMatchedCaseInsensitively()
    {
        var json = """{"name":"Captain Voss","type":"Character","Confidence":"0.5"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["Confidence"]!.GetValue<decimal>(), Is.EqualTo(0.5m));
    }

    [Test]
    public void QuotedPinCoordinates_BecomeNumbers()
    {
        var json = """
            {"name":"Black Harbor","type":"Location",
             "mapPlacemark":{"attachmentId":"11111111-1111-1111-1111-111111111111","x":"0.25","y":"0.75"}}
            """;

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();
        var pin = result["mapPlacemark"]!.AsObject();

        Assert.That(pin["x"]!.GetValue<decimal>(), Is.EqualTo(0.25m));
        Assert.That(pin["y"]!.GetValue<decimal>(), Is.EqualTo(0.75m));
    }

    [Test]
    public void NonNumericConfidence_IsLeftForTheValidatorToReject()
    {
        var json = """{"name":"Captain Voss","type":"Character","confidence":"high"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["confidence"]!.GetValue<string>(), Is.EqualTo("high"));
    }

    [Test]
    public void CommaDecimal_IsNotReinterpreted()
    {
        // Invariant culture on purpose: the model emits JSON, so "0,99" is garbage, not a
        // European decimal. Silently reading it as 0.99 would invent a confidence.
        var json = """{"name":"Captain Voss","type":"Character","confidence":"0,99"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["confidence"]!.GetValue<string>(), Is.EqualTo("0,99"));
    }

    [Test]
    public void NullConfidence_IsUntouched()
    {
        var json = """{"name":"Captain Voss","type":"Character","confidence":null}""";

        Assert.That(ExtractionService.NormalizePayloadFields(json), Is.EqualTo(json));
    }

    #endregion

    #region Name fields

    [Test]
    public void SloppyCreateName_HasItsWhitespaceCollapsed()
    {
        // The exact shape that used to strand a batch: dedup collapsed whitespace, name
        // resolution did not, so the create bound to canon and its facts could never resolve.
        var json = """{"name":"Salt  Factor","type":"Storyline"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["name"]!.GetValue<string>(), Is.EqualTo("Salt Factor"));
    }

    [Test]
    public void SloppyReferenceNames_AreCollapsed()
    {
        var json = "{\"artifactAName\":\"  Captain   Voss \",\"artifactBName\":\"Black\\tHarbor\",\"type\":\"LocatedIn\"}";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["artifactAName"]!.GetValue<string>(), Is.EqualTo("Captain Voss"));
        Assert.That(result["artifactBName"]!.GetValue<string>(), Is.EqualTo("Black Harbor"));
    }

    [Test]
    public void FactArtifactName_IsCollapsed()
    {
        var json = """{"artifactName":"Salt  Factor","predicate":"is","value":"a cartel"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["artifactName"]!.GetValue<string>(), Is.EqualTo("Salt Factor"));
    }

    [Test]
    public void NameCaseIsPreserved()
    {
        // Collapsing is about typos, not about renaming what the GM will see.
        var json = """{"name":"the  SALT  Factor","type":"Storyline"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["name"]!.GetValue<string>(), Is.EqualTo("the SALT Factor"));
    }

    [Test]
    public void ArticlesSurviveNormalization()
    {
        var json = """{"name":"The Salt Factor","type":"Storyline"}""";

        Assert.That(ExtractionService.NormalizePayloadFields(json), Is.EqualTo(json));
    }

    [Test]
    public void BlankName_IsLeftForTheValidatorToReject()
    {
        var json = """{"name":"   ","type":"Storyline"}""";

        var result = JsonNode.Parse(ExtractionService.NormalizePayloadFields(json))!.AsObject();

        Assert.That(result["name"]!.GetValue<string>(), Is.EqualTo("   "),
            "emptying the field would turn a clear validation error into a confusing one");
    }

    [Test]
    public void NullName_IsUntouched()
    {
        var json = """{"name":null,"summary":"A rename that clears nothing."}""";

        Assert.That(ExtractionService.NormalizePayloadFields(json), Is.EqualTo(json));
    }

    #endregion

    [Test]
    public void AlreadyCleanPayload_IsUnchanged()
    {
        var json = """{"name":"Captain Voss","type":"Character","confidence":0.99}""";

        Assert.That(ExtractionService.NormalizePayloadFields(json), Is.EqualTo(json));
    }

    [Test]
    public void MalformedJson_PassesThroughUntouched()
    {
        const string json = "not json at all";

        Assert.That(ExtractionService.NormalizePayloadFields(json), Is.EqualTo(json));
    }
}
