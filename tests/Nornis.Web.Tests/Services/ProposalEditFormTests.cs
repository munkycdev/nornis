using System.Text.Json;
using System.Text.RegularExpressions;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// Review's Edit as a form (3.32). The form shows a few fields of a proposal's payload and
/// must hand back the rest exactly as it found it: the ids, the map coordinates and the
/// confidence are what the applicator acts on, and a form that dropped one would turn an edit
/// to a name into a different proposal.
/// </summary>
[TestFixture]
public class ProposalEditFormTests
{
    private const string CreateLocation =
        """{"name":"Karvosthi","type":"Location","summary":null,"visibility":"GMOnly","confidence":0.82,"mapPlacemark":{"attachmentId":"0b9a4d5e-7c1f-4a55-9a59-0d3c1a6f2b11","x":0.25,"y":0.5,"label":"Karvosti"}}""";

    [Test]
    public void AForm_ShowsTheFieldsAPersonWouldChange_AndNotTheBookkeeping()
    {
        var form = ProposalEditForm.Read("CreateArtifact", CreateLocation)!;

        Assert.Multiple(() =>
        {
            Assert.That(form.Select(f => f.Field.Label), Is.EqualTo((string[])["Name", "Type", "Summary", "Who can see it"]));
            Assert.That(form.Select(f => f.Value), Is.EqualTo((string[])["Karvosthi", "Location", "", "GMOnly"]),
                "a null in the payload reads as an empty box");
        });
    }

    [Test]
    public void Saving_ChangesOnlyWhatWasEdited_AndKeepsEverythingElseInPlace()
    {
        var form = ProposalEditForm.Read("CreateArtifact", CreateLocation)!
            .Select(f => f.Field.Key == "name" ? (f.Field, "Karvosti") : f)
            .ToList();

        var (json, error) = ProposalEditForm.Write(CreateLocation, form);

        Assert.That(error, Is.Null);
        using var before = JsonDocument.Parse(CreateLocation);
        using var after = JsonDocument.Parse(json!);
        Assert.Multiple(() =>
        {
            Assert.That(after.RootElement.GetProperty("name").GetString(), Is.EqualTo("Karvosti"));
            Assert.That(after.RootElement.EnumerateObject().Select(p => p.Name),
                Is.EqualTo(before.RootElement.EnumerateObject().Select(p => p.Name)), "no property added, dropped or moved");
            Assert.That(after.RootElement.GetProperty("confidence").GetRawText(), Is.EqualTo("0.82"));
            Assert.That(after.RootElement.GetProperty("mapPlacemark").GetRawText(),
                Is.EqualTo(before.RootElement.GetProperty("mapPlacemark").GetRawText()), "the pin goes through untouched");
            Assert.That(after.RootElement.GetProperty("summary").ValueKind, Is.EqualTo(JsonValueKind.Null),
                "an empty box stays null, not an empty string the applicator would store");
        });
    }

    [Test]
    public void ARequiredField_CannotBeEmptied()
    {
        var form = ProposalEditForm.Read("CreateArtifact", CreateLocation)!
            .Select(f => f.Field.Key == "name" ? (f.Field, "   ") : f)
            .ToList();

        var (json, error) = ProposalEditForm.Write(CreateLocation, form);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.Null);
            Assert.That(error, Is.EqualTo("Name can't be empty."));
        });
    }

    [Test]
    public void AnOptionalFieldThePayloadLacks_IsAddedWhenFilled_AndLeftOutWhenNot()
    {
        const string updateFact = """{"value":"The plateau was attacked at dusk."}""";
        var form = ProposalEditForm.Read("UpdateFact", updateFact)!;

        var untouched = ProposalEditForm.Write(updateFact, form).Json!;
        var withTruth = ProposalEditForm.Write(updateFact,
            form.Select(f => f.Field.Key == "truthState" ? (f.Field, "Rumor") : f)).Json!;

        Assert.Multiple(() =>
        {
            Assert.That(untouched, Is.EqualTo(updateFact), "showing a proposal in the form does not change it");
            Assert.That(withTruth, Is.EqualTo("""{"value":"The plateau was attacked at dusk.","truthState":"Rumor"}"""));
        });
    }

    [Test]
    public void APropertyWrittenWithAnotherCasing_IsEdited_NotDuplicated()
    {
        // The API reads the payload case-insensitively, so one edited by hand under Advanced
        // may say "Name". A second "name" beside it would leave which one wins to the parser.
        const string byHand = """{"Name":"Captain Vos","Type":"Character"}""";
        var form = ProposalEditForm.Read("CreateArtifact", byHand)!
            .Select(f => f.Field.Key == "name" ? (f.Field, "Captain Voss") : f);

        Assert.That(ProposalEditForm.Write(byHand, form).Json, Is.EqualTo("""{"Name":"Captain Voss","Type":"Character"}"""));
    }

    [Test]
    public void ANameThatStandsInForAnId_IsOfferedOnlyWhenThePayloadCarriesIt()
    {
        // artifactName is how a fact refers to an entry created in the same batch. On a fact
        // that already has its target by id, offering the box would let a reviewer add a
        // second, competing reference.
        var byName = ProposalEditForm.Read("AddFact", """{"predicate":"region","value":"Davokar","artifactName":"Karvosti"}""")!;
        var byId = ProposalEditForm.Read("AddFact", """{"predicate":"region","value":"Davokar"}""")!;

        Assert.Multiple(() =>
        {
            Assert.That(byName.Select(f => f.Field.Key), Does.Contain("artifactName"));
            Assert.That(byId.Select(f => f.Field.Key), Does.Not.Contain("artifactName"));
        });
    }

    [TestCase("SomethingNew", """{"name":"x"}""")]
    [TestCase("CreateArtifact", "[1,2,3]")]
    [TestCase("CreateArtifact", "not json")]
    public void WhatAFormCannotShow_FallsBackToTheJson(string changeType, string json)
    {
        Assert.That(ProposalEditForm.Read(changeType, json), Is.Null);
    }

    [TestCase("CreateArtifact")]
    [TestCase("UpdateArtifact")]
    [TestCase("MergeArtifact")]
    [TestCase("AddFact")]
    [TestCase("UpdateFact")]
    [TestCase("AddRelationship")]
    [TestCase("UpdateRelationship")]
    [TestCase("AddPlacemark")]
    public void EveryFieldAFormShows_IsAPropertyOfThatTypesPayload(string changeType)
    {
        // The form's keys are written by hand against payload records the Web cannot reference.
        // A key that is not on the record would be saved, ignored by the API, and look applied.
        var file = changeType switch
        {
            "AddFact" or "UpdateFact" => "FactPayloads.cs",
            "AddRelationship" or "UpdateRelationship" => "RelationshipPayloads.cs",
            "AddPlacemark" => "PlacemarkPayloads.cs",
            _ => "ArtifactPayloads.cs",
        };
        var source = Read("src", "Nornis.Application", "Validation", file);
        var record = Regex.Match(source, $@"public record {changeType}Payload\((?<body>[^;]*?)\);", RegexOptions.Singleline);
        Assert.That(record.Success, Is.True, $"{changeType}Payload not found in {file}");
        var properties = Regex.Matches(record.Groups["body"].Value, @"\b(?<name>[A-Z]\w*)\s*(?:=\s*null\s*)?(?:,|$)", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .Select(name => char.ToLowerInvariant(name[0]) + name[1..])
            .ToHashSet();

        Assert.That(ProposalEditForm.FieldsFor(changeType).Select(f => f.Key), Is.SubsetOf(properties));
    }

    [Test]
    public void TheChangeTypesWithAForm_AreAllOfThem()
    {
        var types = EnumNames("ReviewChangeType");

        Assert.That(types.Where(t => ProposalEditForm.FieldsFor(t).Count == 0), Is.Empty,
            "a new change type opens on raw JSON until it is given a form");
    }

    [Test]
    public void EveryChangeType_HasItsOwnWordsInReview()
    {
        // The fallback is the enum's name in words ("Create artifact"), which is the leak the
        // labels exist to stop. A new change type must be given a label of its own.
        var types = EnumNames("ReviewChangeType");

        Assert.Multiple(() =>
        {
            Assert.That(types.Select(ProposalEditForm.ChangeLabel), Has.None.Contain("rtifact"));
            Assert.That(types.Select(ProposalEditForm.ChangeLabel), Is.Unique);
            Assert.That(ProposalEditForm.ChangeLabel("CreateArtifact"), Is.EqualTo("New entry"));
            Assert.That(ProposalEditForm.ChangeLabel("SomethingNew"), Is.EqualTo("Something new"));
        });
    }

    [TestCase("CreateArtifact", "visibility", "GMOnly", "Who can see it", "GM only")]
    [TestCase("CreateArtifact", "Visibility", "GMOnly", "Who can see it", "GM only")]
    [TestCase("AddFact", "value", "Shares the quay.", "Fact", "Shares the quay.")]
    [TestCase("AddFact", "truthState", "Rumor", "How sure the record is", "Rumor")]
    [TestCase("AddFact", "truthState", "Whispered", "How sure the record is", "Whispered")]
    [TestCase("MergeArtifact", "sourceArtifactId", "abc", "Source artifact id", "abc")]
    [TestCase("SomethingNew", "name", "Voss", "Name", "Voss")]
    public void AReviewRow_ReadsInTheFormsWords(string changeType, string key, string value, string label, string shown)
    {
        Assert.That(ProposalEditForm.Describe(changeType, key, value), Is.EqualTo((label, shown)));
    }

    [Test]
    public void TheChoices_AreTheDomainsEnums()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProposalEditForm.ArtifactTypes, Is.EqualTo(EnumNames("ArtifactType")));
            Assert.That(ProposalEditForm.ArtifactStatuses, Is.EqualTo(EnumNames("ArtifactStatus")));
            Assert.That(ProposalEditForm.TruthStates, Is.EqualTo(EnumNames("TruthState")));
        });
    }

    private static IReadOnlyList<string> EnumNames(string name)
    {
        var source = Read("src", "Nornis.Domain", "Enums", $"{name}.cs");
        var body = Regex.Match(source, $@"public enum {name}\s*\{{(?<body>[^}}]*)\}}", RegexOptions.Singleline);
        Assert.That(body.Success, Is.True);
        var withoutComments = Regex.Replace(body.Groups["body"].Value, @"//[^\n]*", string.Empty);
        return Regex.Matches(withoutComments, @"\b(?<n>[A-Z]\w*)\b").Select(m => m.Groups["n"].Value).ToList();
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
