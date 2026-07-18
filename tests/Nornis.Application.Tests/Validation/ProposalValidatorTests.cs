using System.Text.Json;
using Nornis.Application.Validation;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Validation;

[TestFixture]
public class ProposalValidatorTests
{
    private ProposalValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new ProposalValidator();
    }

    #region General Validation

    [Test]
    public void ValidateProposedValue_EmptyJson_ReturnsError()
    {
        var result = _validator.ValidateProposedValue("", ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_payload"));
    }

    [Test]
    public void ValidateProposedValue_WhitespaceJson_ReturnsError()
    {
        var result = _validator.ValidateProposedValue("   ", ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_payload"));
    }

    [Test]
    public void ValidateProposedValue_ExceedsMaxLength_ReturnsPayloadTooLarge()
    {
        var json = new string('x', 32_769);

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("payload_too_large"));
    }

    [Test]
    public void ValidateProposedValue_ExactlyMaxLength_DoesNotReturnPayloadTooLarge()
    {
        // 32,768 chars is allowed; build a valid CreateArtifact payload that fits
        var payload = new CreateArtifactPayload("Captain Voss", "Character", null, null, null);
        var json = JsonSerializer.Serialize(payload);

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.Error?.Code, Is.Not.EqualTo("payload_too_large"));
    }

    [Test]
    public void ValidateProposedValue_InvalidJson_ReturnsError()
    {
        var result = _validator.ValidateProposedValue("{not valid json", ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_payload"));
    }

    #endregion

    #region CreateArtifact Validation

    [Test]
    public void CreateArtifact_ValidPayload_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new CreateArtifactPayload(
            "Captain Voss", "Character", "A harbor captain", "PartyVisible", 0.85m));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void CreateArtifact_MinimalValidPayload_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new CreateArtifactPayload(
            "X", "Location", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void CreateArtifact_NullName_ReturnsError()
    {
        var json = """{"name": null, "type": "Character"}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Name"));
    }

    [Test]
    public void CreateArtifact_EmptyName_ReturnsError()
    {
        var json = """{"name": "", "type": "Character"}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Name"));
    }

    [Test]
    public void CreateArtifact_NameExceeds200Chars_ReturnsError()
    {
        var longName = new string('A', 201);
        var json = JsonSerializer.Serialize(new CreateArtifactPayload(longName, "Character", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Name"));
        Assert.That(result.Error!.Message, Does.Contain("200"));
    }

    [Test]
    public void CreateArtifact_NameExactly200Chars_ReturnsSuccess()
    {
        var name = new string('A', 200);
        var json = JsonSerializer.Serialize(new CreateArtifactPayload(name, "Character", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void CreateArtifact_InvalidType_ReturnsError()
    {
        var json = """{"name": "Captain Voss", "type": "InvalidType"}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Type"));
        Assert.That(result.Error!.Message, Does.Contain("InvalidType"));
    }

    [Test]
    public void CreateArtifact_NullType_ReturnsError()
    {
        var json = """{"name": "Captain Voss", "type": null}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Type"));
    }

    [TestCase("Character")]
    [TestCase("Location")]
    [TestCase("Item")]
    [TestCase("Faction")]
    [TestCase("Event")]
    [TestCase("Storyline")]
    [TestCase("Concept")]
    [TestCase("Document")]
    public void CreateArtifact_AllValidArtifactTypes_ReturnSuccess(string type)
    {
        var json = JsonSerializer.Serialize(new CreateArtifactPayload("Test", type, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void CreateArtifact_CaseInsensitiveType_ReturnsSuccess()
    {
        var json = """{"name": "Black Harbor", "type": "location"}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.CreateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region UpdateArtifact Validation

    [Test]
    public void UpdateArtifact_AtLeastOneFieldSet_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new UpdateArtifactPayload("New Name", null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void UpdateArtifact_AllFieldsNull_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new UpdateArtifactPayload(null, null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("At least one field"));
    }

    [Test]
    public void UpdateArtifact_OnlyConfidenceSet_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new UpdateArtifactPayload(null, null, null, 0.9m, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region MergeArtifact Validation

    [Test]
    public void MergeArtifact_ValidSourceArtifactId_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new MergeArtifactPayload(
            Guid.NewGuid(), "Merged Name", "Combined summary", null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.MergeArtifact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void MergeArtifact_EmptyGuidSourceArtifactId_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new MergeArtifactPayload(
            Guid.Empty, "Merged Name", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.MergeArtifact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("SourceArtifactId"));
    }

    #endregion

    #region AddFact Validation

    [Test]
    public void AddFact_ValidPayload_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new AddFactPayload(
            "location", "Black Harbor", 0.8m, "Likely", "PartyVisible"));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddFact_NullPredicate_ReturnsError()
    {
        var json = """{"predicate": null, "value": "Black Harbor"}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Predicate"));
    }

    [Test]
    public void AddFact_EmptyPredicate_ReturnsError()
    {
        var json = """{"predicate": "", "value": "Black Harbor"}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Predicate"));
    }

    [Test]
    public void AddFact_PredicateExceeds500Chars_ReturnsError()
    {
        var longPredicate = new string('P', 501);
        var json = JsonSerializer.Serialize(new AddFactPayload(longPredicate, "value", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Predicate"));
        Assert.That(result.Error!.Message, Does.Contain("500"));
    }

    [Test]
    public void AddFact_PredicateExactly500Chars_ReturnsSuccess()
    {
        var predicate = new string('P', 500);
        var json = JsonSerializer.Serialize(new AddFactPayload(predicate, "value", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddFact_NullValue_ReturnsError()
    {
        var json = """{"predicate": "location", "value": null}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Value"));
    }

    [Test]
    public void AddFact_EmptyValue_ReturnsError()
    {
        var json = """{"predicate": "location", "value": ""}""";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Value"));
    }

    [Test]
    public void AddFact_ValueExceeds4000Chars_ReturnsError()
    {
        var longValue = new string('V', 4001);
        var json = JsonSerializer.Serialize(new AddFactPayload("predicate", longValue, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Value"));
        Assert.That(result.Error!.Message, Does.Contain("4000"));
    }

    [Test]
    public void AddFact_ValueExactly4000Chars_ReturnsSuccess()
    {
        var value = new string('V', 4000);
        var json = JsonSerializer.Serialize(new AddFactPayload("predicate", value, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddFact);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region UpdateFact Validation

    [Test]
    public void UpdateFact_AtLeastOneFieldSet_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new UpdateFactPayload("Updated value", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateFact);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void UpdateFact_AllFieldsNull_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new UpdateFactPayload(null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateFact);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("At least one field"));
    }

    [Test]
    public void UpdateFact_OnlyConfidenceSet_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new UpdateFactPayload(null, 0.95m, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateFact);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region AddRelationship Validation

    [Test]
    public void AddRelationship_NamesInsteadOfIds_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            null, null, "LocatedIn", null, null, null, null,
            ArtifactAName: "Captain Voss", ArtifactBName: "Black Harbor"));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddRelationship_MixedIdAndName_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            Guid.NewGuid(), null, "SuspectedIn", null, null, null, null,
            ArtifactBName: "The Missing Caravan"));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddRelationship_NoIdAndNoNameForEndpointA_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            null, Guid.NewGuid(), "LocatedIn", null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("ArtifactAId or ArtifactAName"));
    }

    [Test]
    public void AddRelationship_ValidPayload_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            Guid.NewGuid(), Guid.NewGuid(), "LocatedIn", "Captain Voss is in Black Harbor", 0.85m, "Likely", "PartyVisible"));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddRelationship_EmptyArtifactAId_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            Guid.Empty, Guid.NewGuid(), "LocatedIn", null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("ArtifactAId"));
    }

    [Test]
    public void AddRelationship_EmptyArtifactBId_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            Guid.NewGuid(), Guid.Empty, "LocatedIn", null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("ArtifactBId"));
    }

    [Test]
    public void AddRelationship_NullType_ReturnsError()
    {
        var json = $@"{{""artifactAId"": ""{Guid.NewGuid()}"", ""artifactBId"": ""{Guid.NewGuid()}"", ""type"": null}}";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Type"));
    }

    [Test]
    public void AddRelationship_EmptyType_ReturnsError()
    {
        var json = $@"{{""artifactAId"": ""{Guid.NewGuid()}"", ""artifactBId"": ""{Guid.NewGuid()}"", ""type"": """"}}";

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Type"));
    }

    [Test]
    public void AddRelationship_TypeExceeds200Chars_ReturnsError()
    {
        var longType = new string('T', 201);
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            Guid.NewGuid(), Guid.NewGuid(), longType, null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("Type"));
        Assert.That(result.Error!.Message, Does.Contain("200"));
    }

    [Test]
    public void AddRelationship_TypeExactly200Chars_ReturnsSuccess()
    {
        var type = new string('T', 200);
        var json = JsonSerializer.Serialize(new AddRelationshipPayload(
            Guid.NewGuid(), Guid.NewGuid(), type, null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.AddRelationship);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region UpdateRelationship Validation

    [Test]
    public void UpdateRelationship_AtLeastOneFieldSet_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new UpdateRelationshipPayload(
            "SuspectedIn", null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateRelationship);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void UpdateRelationship_AllFieldsNull_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new UpdateRelationshipPayload(null, null, null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateRelationship);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("At least one field"));
    }

    [Test]
    public void UpdateRelationship_OnlyDescriptionSet_ReturnsSuccess()
    {
        var json = JsonSerializer.Serialize(new UpdateRelationshipPayload(
            null, "Updated description", null, null, null));

        var result = _validator.ValidateProposedValue(json, ReviewChangeType.UpdateRelationship);

        Assert.That(result.IsSuccess, Is.True);
    }

    #endregion

    #region AddPlacemark

    private static string PlacemarkJson(Guid? artifactId = null, string? artifactName = null,
        Guid? attachmentId = null, decimal x = 0.5m, decimal y = 0.5m, string? label = "Ironhold") =>
        JsonSerializer.Serialize(new
        {
            artifactId,
            artifactName,
            attachmentId = attachmentId ?? Guid.NewGuid(),
            x, y, label
        });

    [Test]
    public void AddPlacemark_Valid_ById_Succeeds()
    {
        var result = _validator.ValidateProposedValue(
            PlacemarkJson(artifactId: Guid.NewGuid()), ReviewChangeType.AddPlacemark);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddPlacemark_Valid_ByName_Succeeds()
    {
        var result = _validator.ValidateProposedValue(
            PlacemarkJson(artifactName: "Ironhold"), ReviewChangeType.AddPlacemark);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddPlacemark_MissingAttachment_Fails()
    {
        var result = _validator.ValidateProposedValue(
            PlacemarkJson(artifactId: Guid.NewGuid(), attachmentId: Guid.Empty), ReviewChangeType.AddPlacemark);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void AddPlacemark_NoArtifactReference_Fails()
    {
        var result = _validator.ValidateProposedValue(
            PlacemarkJson(), ReviewChangeType.AddPlacemark);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void AddPlacemark_OutOfRangeCoordinate_Fails()
    {
        var result = _validator.ValidateProposedValue(
            PlacemarkJson(artifactId: Guid.NewGuid(), x: 1.5m), ReviewChangeType.AddPlacemark);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void AddPlacemark_LabelTooLong_Fails()
    {
        var result = _validator.ValidateProposedValue(
            PlacemarkJson(artifactId: Guid.NewGuid(), label: new string('x', 201)), ReviewChangeType.AddPlacemark);

        Assert.That(result.IsSuccess, Is.False);
    }

    #endregion
}
