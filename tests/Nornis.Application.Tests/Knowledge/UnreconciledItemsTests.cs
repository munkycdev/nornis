using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Knowledge;

[TestFixture]
public class UnreconciledItemsTests
{
    private static Artifact Artifact(ArtifactType type, string name) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = Guid.NewGuid(),
        Type = type,
        Name = name,
        Status = ArtifactStatus.Active,
        Visibility = VisibilityScope.PartyVisible,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static CharacterRecord Record(params Artifact[] connected) =>
        new(
            ArtifactId: Guid.NewGuid(),
            ArtifactName: "Tavrin",
            Summary: null,
            Facts: [],
            TotalFactCount: 0,
            Groups: connected
                .GroupBy(a => a.Type)
                .Select(g => new CharacterRecordGroup(g.Key, g.ToList(), g.Count()))
                .ToList());

    [Test]
    public void NamesTheItemsTheSheetDoesNotMention()
    {
        var key = Artifact(ArtifactType.Item, "Silver Key");
        var cloak = Artifact(ArtifactType.Item, "Ashen Cloak");

        var result = UnreconciledItems.Find(Record(key, cloak), "Equipment: silver key, rations, rope");

        Assert.That(result.Select(u => u.Name), Is.EqualTo(["Ashen Cloak"]));
        Assert.That(result.Single().ArtifactId, Is.EqualTo(cloak.Id));
    }

    /// <summary>
    /// The whole comparison: a case-insensitive presence test of the name in the text. It does
    /// not parse, count, or infer, and this test pins it to that — a smarter match that also
    /// passed here would be a parser, which is the rules engine this feature declines to be.
    /// </summary>
    [Test]
    public void MatchesByPlainPresenceOnly()
    {
        var key = Artifact(ArtifactType.Item, "Silver Key");

        Assert.Multiple(() =>
        {
            Assert.That(UnreconciledItems.Find(Record(key), "SILVER KEY (lost?)"), Is.Empty, "case-insensitive");
            Assert.That(UnreconciledItems.Find(Record(key), "silver keys ×2"), Is.Empty, "substring counts as present");
            Assert.That(UnreconciledItems.Find(Record(key), "a key, silver"), Has.Count.EqualTo(1), "no word-level cleverness");
        });
    }

    /// <summary>
    /// No sheet means no observation, not "everything is missing". A blank page has nothing to
    /// be reconciled against, and a reader who may not open the sheet gets null for it — the
    /// same null — so this is also what keeps the observation from saying anything about a
    /// sheet the reader cannot read.
    /// </summary>
    [Test]
    public void NoReadableSheet_ReportsNothing()
    {
        var record = Record(Artifact(ArtifactType.Item, "Silver Key"));

        Assert.Multiple(() =>
        {
            Assert.That(UnreconciledItems.Find(record, null), Is.Empty);
            Assert.That(UnreconciledItems.Find(record, "   "), Is.Empty);
            Assert.That(UnreconciledItems.Find(null, "Silver Key"), Is.Empty);
        });
    }

    [Test]
    public void OnlyObservedTypesAreReconciled()
    {
        var record = Record(
            Artifact(ArtifactType.Item, "Silver Key"),
            Artifact(ArtifactType.Location, "Bleakspire Keep"),
            Artifact(ArtifactType.Character, "Brother Alder"),
            Artifact(ArtifactType.Faction, "Bellwardens"));

        var result = UnreconciledItems.Find(record, "nothing written yet, really");

        Assert.That(result.Select(u => u.Name), Is.EqualTo(["Silver Key"]));
        Assert.That(UnreconciledItems.ObservedTypes, Is.EqualTo([ArtifactType.Item]),
            "widening the observed types is a product decision, not a refactor — see the design's note on noise");
    }

    /// <summary>
    /// Reads only what the record chose to show, so the projector's per-group cap bounds this
    /// too. If the record were ever handed uncapped, this would be the test that noticed.
    /// </summary>
    [Test]
    public void BoundedByTheRecordsOwnCap()
    {
        var many = Enumerable.Range(0, CharacterRecordProjector.MaxPerGroup + 10)
            .Select(i => Artifact(ArtifactType.Item, $"Item {i:00}"))
            .ToList();
        var detail = new ArtifactDetail(
            Artifact: Artifact(ArtifactType.Character, "Tavrin"),
            Facts: [],
            Relationships: [],
            ConnectedArtifacts: many,
            SourceReferences: [],
            SourceTitles: new Dictionary<Guid, string>(),
            SourceSlugs: new Dictionary<Guid, string?>(),
            PlayedBy: []);

        var result = UnreconciledItems.Find(CharacterRecordProjector.Project(detail), "blank-ish sheet");

        Assert.That(result, Has.Count.EqualTo(CharacterRecordProjector.MaxPerGroup));
    }
}
