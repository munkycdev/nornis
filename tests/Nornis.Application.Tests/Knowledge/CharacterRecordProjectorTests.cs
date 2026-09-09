using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Knowledge;

[TestFixture]
public class CharacterRecordProjectorTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private static Artifact Artifact(ArtifactType type, string name) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Type = type,
        Name = name,
        Status = ArtifactStatus.Active,
        Visibility = VisibilityScope.PartyVisible,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ArtifactDetail Detail(
        IEnumerable<Artifact>? connected = null,
        IEnumerable<ArtifactFact>? facts = null) =>
        new(
            Artifact: Artifact(ArtifactType.Character, "Tavrin"),
            Facts: facts?.ToList() ?? [],
            Relationships: [],
            ConnectedArtifacts: connected?.ToList() ?? [],
            SourceReferences: [],
            SourceTitles: new Dictionary<Guid, string>(),
            PlayedBy: []);

    private static ArtifactFact Fact(int index) => new()
    {
        Id = Guid.NewGuid(),
        ArtifactId = Guid.NewGuid(),
        Predicate = $"predicate {index}",
        Value = $"value {index}",
        TruthState = TruthState.Confirmed,
        Visibility = VisibilityScope.PartyVisible,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Test]
    public void Project_OrdersGroupsForReadingAboutAPerson()
    {
        var detail = Detail(
        [
            Artifact(ArtifactType.Storyline, "The Missing Caravan"),
            Artifact(ArtifactType.Item, "Silver Key"),
            Artifact(ArtifactType.Faction, "Harbour Watch"),
            Artifact(ArtifactType.Location, "Black Harbor")
        ]);

        var record = CharacterRecordProjector.Project(detail);

        Assert.That(record.Groups.Select(g => g.Type), Is.EqualTo(
        [
            ArtifactType.Item,
            ArtifactType.Location,
            ArtifactType.Faction,
            ArtifactType.Storyline
        ]).AsCollection);
    }

    [Test]
    public void Project_PlacesUnrankedTypesAfterTheNamedOnes()
    {
        var detail = Detail(
        [
            Artifact(ArtifactType.Concept, "The Long Silence"),
            Artifact(ArtifactType.Item, "Silver Key")
        ]);

        var record = CharacterRecordProjector.Project(detail);

        Assert.That(record.Groups.First().Type, Is.EqualTo(ArtifactType.Item));
        Assert.That(record.Groups.Last().Type, Is.EqualTo(ArtifactType.Concept));
    }

    [Test]
    public void Project_CapsEachGroupButReportsTheTrueTotal()
    {
        var overCap = Enumerable
            .Range(0, CharacterRecordProjector.MaxPerGroup + 5)
            .Select(i => Artifact(ArtifactType.Item, $"Item {i:D2}"));

        var record = CharacterRecordProjector.Project(Detail(overCap));

        var items = record.Groups.Single();
        Assert.Multiple(() =>
        {
            Assert.That(items.Artifacts, Has.Count.EqualTo(CharacterRecordProjector.MaxPerGroup));
            Assert.That(items.TotalCount, Is.EqualTo(CharacterRecordProjector.MaxPerGroup + 5));
        });
    }

    [Test]
    public void Project_CapsFactsButReportsTheTrueTotal()
    {
        var facts = Enumerable.Range(0, CharacterRecordProjector.MaxFacts + 3).Select(Fact);

        var record = CharacterRecordProjector.Project(Detail(facts: facts));

        Assert.Multiple(() =>
        {
            Assert.That(record.Facts, Has.Count.EqualTo(CharacterRecordProjector.MaxFacts));
            Assert.That(record.TotalFactCount, Is.EqualTo(CharacterRecordProjector.MaxFacts + 3));
        });
    }

    [Test]
    public void Project_SortsWithinAGroupByName()
    {
        var detail = Detail(
        [
            Artifact(ArtifactType.Item, "Silver Key"),
            Artifact(ArtifactType.Item, "Ashen Cloak")
        ]);

        var record = CharacterRecordProjector.Project(detail);

        Assert.That(record.Groups.Single().Artifacts.Select(a => a.Name),
            Is.EqualTo(["Ashen Cloak", "Silver Key"]).AsCollection);
    }

    [Test]
    public void Project_EmptyDetail_ProducesNoGroups()
    {
        var record = CharacterRecordProjector.Project(Detail());

        Assert.That(record.Groups, Is.Empty);
        Assert.That(record.TotalFactCount, Is.Zero);
    }
}
