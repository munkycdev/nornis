using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactNameCandidatesTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Test]
    public void Rank_FindsTheFullerNameFromTheShorterOne()
    {
        var candidates = Rank([Artifact("Kaelen Vorr")], "Kaelen");

        Assert.That(candidates.Single().Name, Is.EqualTo("Kaelen Vorr"));
    }

    [Test]
    public void Rank_FindsTheShorterNameFromTheFullerOne()
    {
        // The direction global search cannot do: the term is longer than the name it should
        // match, so scoring only one way returns nothing.
        var candidates = Rank([Artifact("Kaelen")], "Kaelen Vorr");

        Assert.That(candidates.Single().Name, Is.EqualTo("Kaelen"));
    }

    [Test]
    public void Rank_FindsAcrossALeadingArticle()
    {
        var candidates = Rank([Artifact("The Salt Factor")], "Salt Factor");

        Assert.That(candidates.Single().Name, Is.EqualTo("The Salt Factor"));
    }

    [Test]
    public void Rank_IgnoresASubstringBuriedInALongerName()
    {
        Assert.That(Rank([Artifact("Ironvossen Guild")], "Voss"), Is.Empty);
    }

    [Test]
    public void Rank_IgnoresASummaryMention()
    {
        var mentions = Artifact("Black Harbor");
        mentions.Summary = "Captain Voss keeps order here.";

        Assert.That(Rank([mentions], "Voss"), Is.Empty);
    }

    [Test]
    public void Rank_IgnoresArchivedMergeLeftovers()
    {
        var archived = Artifact("Captain Voss");
        archived.Status = ArtifactStatus.Archived;

        Assert.That(Rank([archived], "Voss"), Is.Empty);
    }

    [Test]
    public void Rank_IgnoresWhatTheReaderCannotSee()
    {
        var hidden = Artifact("Captain Voss");
        hidden.Visibility = VisibilityScope.GMOnly;

        var asPlayer = ArtifactNameCandidates.Rank(
            [hidden], "Voss", VisibilityFilter.ForRole(WorldRole.Player, Guid.NewGuid()));

        Assert.That(asPlayer, Is.Empty);
    }

    [Test]
    public void Rank_PutsTheStrongerResemblanceFirst()
    {
        var candidates = Rank([Artifact("Vossberg Keep"), Artifact("The Voss Ledger")], "Voss");

        Assert.That(candidates.Select(a => a.Name),
            Is.EqualTo(["Vossberg Keep", "The Voss Ledger"]),
            "a shared prefix outranks the word appearing mid-name");
    }

    [Test]
    public void Rank_CapsWhatItPutsInFrontOfAReviewer()
    {
        var many = Enumerable.Range(0, 20).Select(i => Artifact($"Voss {i}")).ToList();

        Assert.That(Rank(many, "Voss"), Has.Count.EqualTo(ArtifactNameCandidates.MaxCandidates));
    }

    [Test]
    public void Rank_HasNothingToSayAboutABlankName()
    {
        Assert.That(Rank([Artifact("Captain Voss")], "   "), Is.Empty);
    }

    private static IReadOnlyList<Artifact> Rank(IEnumerable<Artifact> artifacts, string name) =>
        ArtifactNameCandidates.Rank(artifacts, name, VisibilityFilter.All);

    private static Artifact Artifact(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = OwnerId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
}
