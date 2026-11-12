using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The candidate rules of the check-in (feature 44), on hand-built evidence: which storylines,
/// entries and predicates Nornis has reason to doubt, in what order, with what evidence.
/// </summary>
[TestFixture]
public class CheckInCandidatesTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.NewGuid();

    private readonly List<Artifact> _artifacts = [];
    private readonly List<ArtifactFact> _facts = [];
    private readonly List<ArtifactRelationship> _relationships = [];
    private readonly List<SourceReference> _references = [];
    private readonly List<Source> _sources = [];
    private readonly Dictionary<Guid, int> _quiet = [];
    private readonly HashSet<Guid> _recentlyAnswered = [];
    private readonly HashSet<string> _settled = new(StringComparer.Ordinal);

    [SetUp]
    public void SetUp()
    {
        _artifacts.Clear();
        _facts.Clear();
        _relationships.Clear();
        _references.Clear();
        _sources.Clear();
        _quiet.Clear();
        _recentlyAnswered.Clear();
        _settled.Clear();
    }

    private CheckInEvidence Evidence() => new(
        _artifacts, _facts, _relationships, _references, _sources,
        Sessions: _sources.Where(s => s.OccurredAt is not null).OrderByDescending(s => s.OccurredAt).ToList(),
        QuietStorylines: _quiet,
        RecentlyAnsweredArtifactIds: _recentlyAnswered,
        SettledPredicates: _settled);

    private static CheckInOptions Options() => new();

    private IReadOnlyList<CheckInCandidate> Build() => CheckInCandidates.Build(Evidence(), Options());

    private Source Session(int day, string? title = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = title ?? $"Session {day}",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            OccurredAt = Base.AddDays(day),
            CreatedAt = Base.AddDays(day),
        };
        _sources.Add(source);
        return source;
    }

    private Artifact Artifact(string name, ArtifactType type, ArtifactStatus status = ArtifactStatus.Active, string? summary = null, int createdDay = 0)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = type,
            Name = name,
            Summary = summary,
            Status = status,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = Base.AddDays(createdDay),
            UpdatedAt = Base.AddDays(createdDay),
        };
        _artifacts.Add(artifact);
        return artifact;
    }

    private ArtifactFact Fact(Artifact artifact, string predicate, string value, Source? from = null)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = predicate,
            Value = value,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = Base,
            UpdatedAt = Base,
        };
        _facts.Add(fact);
        if (from is not null)
        {
            Reference(from, SourceReferenceTargetType.ArtifactFact, fact.Id);
        }
        return fact;
    }

    private void Reference(Source source, SourceReferenceTargetType targetType, Guid targetId) =>
        _references.Add(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = source.OccurredAt ?? Base,
            Source = source,
        });

    private void Relate(Artifact a, Artifact b, string type = "Involves") =>
        _relationships.Add(new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = a.Id,
            ArtifactBId = b.Id,
            Type = type,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = Base,
            UpdatedAt = Base,
        });

    // ------------------------------------------------------------------- Storylines --

    [Test]
    public void QuietStoryline_IsACandidate_WithItsEvidence()
    {
        var s1 = Session(1);
        var s2 = Session(2);
        var arc = Artifact("The Drowned Prophecy", ArtifactType.Storyline);
        Fact(arc, "development", "The tide bell rang", s1);
        Fact(arc, "development", "The priest fled", s2);
        Fact(arc, "open question", "Who rang the bell?");
        _quiet[arc.Id] = 4;

        var candidates = Build();

        var c = candidates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(c.Key, Is.EqualTo("c1"));
            Assert.That(c.Kind, Is.EqualTo(CheckInQuestionKind.Storyline));
            Assert.That(c.SubjectArtifactId, Is.EqualTo(arc.Id));
            Assert.That(c.Text, Does.Contain("The Drowned Prophecy"));
            Assert.That(c.Why, Is.EqualTo("Quiet for 4 sessions; 1 open question."));
            Assert.That(c.Evidence.SessionsQuiet, Is.EqualTo(4));
            Assert.That(c.Evidence.SourceCount, Is.EqualTo(2));
            Assert.That(c.Evidence.OpenQuestionCount, Is.EqualTo(1));
            Assert.That(c.Evidence.FirstSeenTitle, Is.EqualTo("Session 1"));
            Assert.That(c.Evidence.Facts, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void OneSourceStoryline_IsACandidate_EvenWhenItIsNotQuiet()
    {
        var s1 = Session(1, "The witch's warning");
        var arc = Artifact("Koves's Curse", ArtifactType.Storyline);
        Fact(arc, "development", "A witch warned Koves", s1);

        var c = Build().Single();

        Assert.That(c.Why, Does.StartWith("Opened from “The witch's warning”, nothing since"));
        Assert.That(c.Evidence.SourceCount, Is.EqualTo(1));
    }

    [Test]
    public void StorylineWithSeveralSources_ThatIsNotQuiet_IsNotACandidate()
    {
        var arc = Artifact("Harbor Smuggling", ArtifactType.Storyline);
        Fact(arc, "development", "Crates on the quay", Session(1));
        Fact(arc, "development", "The customs man paid", Session(2));

        Assert.That(Build(), Is.Empty);
    }

    [Test]
    public void HandMadeStoryline_WithNoSourceBehindIt_IsNotAOneSourceCandidate()
    {
        var arc = Artifact("The GM's own arc", ArtifactType.Storyline);
        Fact(arc, "goal", "Reach the capital");

        Assert.That(Build(), Is.Empty);
    }

    [Test]
    public void DormantResolvedAndRecentlyAnsweredStorylines_AreNotCandidates()
    {
        var s1 = Session(1);
        var dormant = Artifact("Dormant", ArtifactType.Storyline, ArtifactStatus.Dormant);
        var resolved = Artifact("Resolved", ArtifactType.Storyline, ArtifactStatus.Resolved);
        var answered = Artifact("Answered", ArtifactType.Storyline);
        foreach (var arc in new[] { dormant, resolved, answered })
        {
            Fact(arc, "development", "once", s1);
            _quiet[arc.Id] = 5;
        }
        _recentlyAnswered.Add(answered.Id);

        Assert.That(Build(), Is.Empty);
    }

    [Test]
    public void Storylines_RankByQuietPlusOneSourcePlusOpenQuestions_AndAreCapped()
    {
        var s1 = Session(1);
        var s2 = Session(2);
        var quietOnly = Artifact("Quiet", ArtifactType.Storyline);
        Fact(quietOnly, "development", "a", s1);
        Fact(quietOnly, "development", "b", s2);
        _quiet[quietOnly.Id] = 2;

        var oneSourceQuiet = Artifact("One source and quiet", ArtifactType.Storyline);
        Fact(oneSourceQuiet, "development", "a", s1);
        _quiet[oneSourceQuiet.Id] = 2;

        var oneSourceWithQuestions = Artifact("One source, questions", ArtifactType.Storyline);
        Fact(oneSourceWithQuestions, "development", "a", s1);
        Fact(oneSourceWithQuestions, "open question", "Why?");
        Fact(oneSourceWithQuestions, "open question", "Who?");
        _quiet[oneSourceWithQuestions.Id] = 1;

        var ranked = Build().Select(c => c.SubjectLabel).ToList();

        // 2+3+0 = 5, 1+3+2 = 6, 2+0+0 = 2.
        Assert.That(ranked, Is.EqualTo(["One source, questions", "One source and quiet", "Quiet"]));

        var capped = CheckInCandidates.Build(Evidence(), new CheckInOptions { MaxStorylineCandidates = 1 });
        Assert.That(capped.Select(c => c.SubjectLabel), Is.EqualTo(["One source, questions"]));
    }

    [Test]
    public void RelatedEntries_LinkedPeopleFirst_ThenThoseNamedInTheText_NeverPlaces()
    {
        var s1 = Session(1);
        var arc = Artifact("The Warning", ArtifactType.Storyline, summary: "A witch warned Koves about the Ashen Hand near Blackmere.");
        Fact(arc, "development", "Mira heard it too", s1);
        var mira = Artifact("Mira", ArtifactType.Character);
        var koves = Artifact("Koves", ArtifactType.Character);
        var ashenHand = Artifact("Ashen Hand", ArtifactType.Faction);
        Artifact("Blackmere", ArtifactType.Location);
        Artifact("Al", ArtifactType.Character);
        Artifact("Kovesh", ArtifactType.Character);
        Relate(arc, mira);

        var related = Build().Single().Evidence.RelatedEntries;

        Assert.That(related.Select(r => r.Name), Is.EqualTo(["Mira", "Koves", "Ashen Hand"]));
        Assert.That(related.Select(r => r.Id), Is.EqualTo([mira.Id, koves.Id, ashenHand.Id]));
    }

    // ---------------------------------------------------------------------- Entries --

    [Test]
    public void OneSourceEntry_FromBeforeTheLastCadenceSessions_IsACandidate_ARecentOneIsNot()
    {
        var old = Session(1, "The first night");
        Session(2);
        Session(3);
        var recent = Session(4);
        var lamp = Artifact("Old Lamp", ArtifactType.Item);
        Fact(lamp, "material", "brass", old);
        var newcomer = Artifact("Newcomer", ArtifactType.Character);
        Reference(recent, SourceReferenceTargetType.Artifact, newcomer.Id);

        var candidates = Build();

        var c = candidates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(c.Kind, Is.EqualTo(CheckInQuestionKind.Entry));
            Assert.That(c.SubjectArtifactId, Is.EqualTo(lamp.Id));
            Assert.That(c.Text, Is.EqualTo("Keep “Old Lamp” (Item) in the record?"));
            Assert.That(c.Why, Is.EqualTo("Mentioned once, in “The first night”; 1 fact."));
            Assert.That(c.Evidence.FirstSeenTitle, Is.EqualTo("The first night"));
        });
    }

    [Test]
    public void EntriesWithNoSourceOrSeveral_AreNotCandidates()
    {
        var s1 = Session(1);
        var s2 = Session(2);
        Session(3);
        Session(4);
        Session(5);
        Artifact("Hand-made", ArtifactType.Location);
        var seenTwice = Artifact("Seen twice", ArtifactType.Character);
        Reference(s1, SourceReferenceTargetType.Artifact, seenTwice.Id);
        Fact(seenTwice, "mood", "grim", s2);

        Assert.That(Build(), Is.Empty);
    }

    [Test]
    public void Entries_RankFewestFactsFirst_AndAreCapped()
    {
        var old = Session(1);
        Session(2);
        Session(3);
        Session(4);
        var rich = Artifact("Rich", ArtifactType.Character, createdDay: 1);
        Fact(rich, "a", "1", old);
        Fact(rich, "b", "2", old);
        var bare = Artifact("Bare", ArtifactType.Character, createdDay: 2);
        Reference(old, SourceReferenceTargetType.Artifact, bare.Id);

        Assert.That(Build().Select(c => c.SubjectLabel), Is.EqualTo(["Bare", "Rich"]));

        var capped = CheckInCandidates.Build(Evidence(), new CheckInOptions { MaxEntryCandidates = 1 });
        Assert.That(capped.Select(c => c.SubjectLabel), Is.EqualTo(["Bare"]));
    }

    // --------------------------------------------------------------------- Patterns --

    [Test]
    public void Pattern_NeedsEnoughFactsAcrossEnoughEntries_AndIsLabelledAsMostOftenWritten()
    {
        var a = Artifact("A", ArtifactType.Character);
        var b = Artifact("B", ArtifactType.Character);
        var c = Artifact("C", ArtifactType.Character);
        var spellings = new[] { "eye colour", "eye colour", "Eye colour", "eye  colour", "eye colour", "eye colour ", "eye colour", "eye colour" };
        for (var i = 0; i < spellings.Length; i++)
        {
            Fact(new[] { a, b, c }[i % 3], spellings[i], $"shade {i}");
        }

        var pattern = Build().Single();

        Assert.Multiple(() =>
        {
            Assert.That(pattern.Kind, Is.EqualTo(CheckInQuestionKind.Pattern));
            Assert.That(pattern.SubjectArtifactId, Is.Null);
            Assert.That(pattern.SubjectLabel, Is.EqualTo("eye colour"));
            Assert.That(pattern.Text, Is.EqualTo("Keep recording “eye colour”?"));
            Assert.That(pattern.Why, Does.StartWith("8 facts across 3 entries, e.g. shade 0; shade 1; shade 2"));
            Assert.That(pattern.Evidence.FactCount, Is.EqualTo(8));
            Assert.That(pattern.Evidence.ArtifactCount, Is.EqualTo(3));
            Assert.That(pattern.Evidence.Examples, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Pattern_TooFewEntries_StructuralPredicates_AndSettledOnes_AreNotCandidates()
    {
        var a = Artifact("A", ArtifactType.Character);
        var b = Artifact("B", ArtifactType.Character);
        var c = Artifact("C", ArtifactType.Storyline);
        for (var i = 0; i < 8; i++)
        {
            Fact(i % 2 == 0 ? a : b, "two entries only", $"v{i}");
            Fact(new[] { a, b, c }[i % 3], "open question", $"q{i}?");
            Fact(new[] { a, b, c }[i % 3], "goal", $"g{i}");
            Fact(new[] { a, b, c }[i % 3], "Settled Thing", $"s{i}");
        }
        _settled.Add("settled thing");

        Assert.That(Build(), Is.Empty);
    }

    [Test]
    public void Keys_ArePositional_AcrossKinds()
    {
        var old = Session(1);
        Session(2);
        Session(3);
        Session(4);
        var arc = Artifact("Arc", ArtifactType.Storyline);
        Fact(arc, "development", "a", old);
        var lamp = Artifact("Lamp", ArtifactType.Item);
        Reference(old, SourceReferenceTargetType.Artifact, lamp.Id);

        var keys = Build().Select(c => (c.Key, c.Kind)).ToList();

        Assert.That(keys, Is.EqualTo([("c1", CheckInQuestionKind.Storyline), ("c2", CheckInQuestionKind.Entry)]));
        Assert.That(CheckInCandidates.PredicateKey("  Eye   Colour "), Is.EqualTo("eye colour"));
    }
}
