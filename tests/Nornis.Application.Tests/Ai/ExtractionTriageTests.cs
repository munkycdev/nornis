using Nornis.Application.Ai;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// Feature 43. Triage is the one place an Incidental proposal is dropped; these pin the rule
/// and its one exception, a create that a kept proposal needs.
/// </summary>
[TestFixture]
public class ExtractionTriageTests
{
    private static ExtractionProposal Create(string name, ProposalSignificance significance, string type = "Character") => new()
    {
        ChangeType = "CreateArtifact",
        TargetType = "Artifact",
        ProposedValue = new { name, type, visibility = "PartyVisible" },
        Rationale = $"{name} is named in the source.",
        Confidence = 0.9m,
        Significance = significance
    };

    private static ExtractionProposal Fact(string artifactName, ProposalSignificance significance) => new()
    {
        ChangeType = "AddFact",
        TargetType = "ArtifactFact",
        ProposedValue = new { artifactName, predicate = "regarded as", value = "unnatural", visibility = "PartyVisible" },
        Rationale = "The witch said so.",
        Confidence = 0.9m,
        Significance = significance
    };

    private static ExtractionProposal Relationship(string a, string b, ProposalSignificance significance) => new()
    {
        ChangeType = "AddRelationship",
        TargetType = "ArtifactRelationship",
        ProposedValue = new { artifactAName = a, artifactBName = b, type = "WarnedAbout", visibility = "PartyVisible" },
        Rationale = "The warning.",
        Confidence = 0.9m,
        Significance = significance
    };

    [Test]
    public void NothingIncidental_ReturnsTheSameListUntouched()
    {
        var proposals = new[] { Create("Captain Voss", ProposalSignificance.Core), Fact("Captain Voss", ProposalSignificance.Supporting) };

        var result = ExtractionTriage.Apply(proposals);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kept, Is.SameAs(proposals));
            Assert.That(result.DroppedCount, Is.Zero);
            Assert.That(result.DroppedCreateNames, Is.Empty);
        });
    }

    [Test]
    public void Incidental_IsDropped_CoreAndSupportingKept_InOrder()
    {
        var core = Create("Captain Voss", ProposalSignificance.Core);
        var incidental = Create("A passing fishmonger", ProposalSignificance.Incidental);
        var supporting = Fact("Captain Voss", ProposalSignificance.Supporting);

        var result = ExtractionTriage.Apply([core, incidental, supporting]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kept, Is.EqualTo([core, supporting]));
            Assert.That(result.DroppedCount, Is.EqualTo(1));
            Assert.That(result.DroppedCreateNames, Is.EqualTo(["A passing fishmonger"]));
        });
    }

    [Test]
    public void AllIncidental_LeavesNothing()
    {
        var result = ExtractionTriage.Apply([
            Create("The tavern cat", ProposalSignificance.Incidental),
            Fact("The tavern cat", ProposalSignificance.Incidental)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kept, Is.Empty);
            Assert.That(result.DroppedCount, Is.EqualTo(2));
            Assert.That(result.DroppedCreateNames, Is.EqualTo(["The tavern cat"]));
        });
    }

    [Test]
    public void IncidentalCreate_NamedByAKeptFact_IsKept()
    {
        // The model called the witch incidental but her warning Core. The fact needs her, so
        // she stays; otherwise accept-time resolution meets a name nothing proposed.
        var witch = Create("Grandmother Hespe", ProposalSignificance.Incidental);
        var warning = Fact("Grandmother Hespe", ProposalSignificance.Core);

        var result = ExtractionTriage.Apply([witch, warning]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kept, Is.EqualTo([witch, warning]));
            Assert.That(result.DroppedCount, Is.Zero);
            Assert.That(result.DroppedCreateNames, Is.Empty);
        });
    }

    [Test]
    public void IncidentalCreate_NamedByAKeptRelationship_IsKept_UnderNameKeyPolicy()
    {
        // Case and whitespace differences are the same name, the way dedup reads them.
        var witch = Create("grandmother  hespe", ProposalSignificance.Incidental);
        var koves = Create("Koves", ProposalSignificance.Core);
        var warning = Relationship("Grandmother Hespe", "Koves", ProposalSignificance.Core);

        var result = ExtractionTriage.Apply([witch, koves, warning]);

        Assert.That(result.Kept, Is.EqualTo([witch, koves, warning]));
    }

    [Test]
    public void IncidentalCreate_NamedOnlyByAnotherIncidentalProposal_IsStillDropped()
    {
        var cat = Create("The tavern cat", ProposalSignificance.Incidental);
        var catFact = Fact("The tavern cat", ProposalSignificance.Incidental);
        var voss = Create("Captain Voss", ProposalSignificance.Core);

        var result = ExtractionTriage.Apply([cat, catFact, voss]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kept, Is.EqualTo([voss]));
            Assert.That(result.DroppedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void IncidentalNonCreate_IsDroppedEvenWhenItNamesAKeptArtifact()
    {
        // Only a create can be promoted: a kept artifact does not need an incidental fact.
        var voss = Create("Captain Voss", ProposalSignificance.Core);
        var trivia = Fact("Captain Voss", ProposalSignificance.Incidental);

        var result = ExtractionTriage.Apply([voss, trivia]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kept, Is.EqualTo([voss]));
            Assert.That(result.DroppedCreateNames, Is.Empty);
        });
    }

    [Test]
    public void Default_Significance_IsSupporting_SoAnUnmarkedProposalIsKept()
    {
        var unmarked = new ExtractionProposal
        {
            ChangeType = "CreateArtifact",
            TargetType = "Artifact",
            ProposedValue = new { name = "Black Harbor", type = "Location" },
            Rationale = "Named.",
            Confidence = 0.8m
        };

        Assert.That(unmarked.Significance, Is.EqualTo(ProposalSignificance.Supporting));
        Assert.That(ExtractionTriage.Apply([unmarked, Create("x", ProposalSignificance.Incidental)]).Kept, Is.EqualTo([unmarked]));
    }
}
