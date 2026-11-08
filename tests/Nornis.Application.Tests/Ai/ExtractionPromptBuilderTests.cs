using Nornis.Application.Ai;
using Nornis.Application.Knowledge;
using Nornis.Application.Prep;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// These tests lived in ExtractionAiClientTests while the vendor adapter owned
/// the prompt text; they moved with the text when the seam converged on Application-built
/// strings. The adapter's own tests now cover only transport, timeout, and parse.
/// </summary>
[TestFixture]
public class ExtractionPromptBuilderTests
{
    private static readonly ExtractionRequest DefaultRequest = new()
    {
        SourceBody = "We questioned Captain Voss in Black Harbor.",
        SourceTitle = "Session 5 Notes",
        SourceType = "SessionNote",
        SourceVisibility = "PartyVisible"
    };

    #region System Prompt Tests

    [Test]
    public void BuildSystemPrompt_IncludesVisibilityInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "GMOnly"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("GMOnly"));
        Assert.That(prompt, Does.Contain("visibility"));
        Assert.That(prompt, Does.Contain("Never produce a proposal with visibility broader than its source"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesTruthStateInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Confirmed"));
        Assert.That(prompt, Does.Contain("Likely"));
        Assert.That(prompt, Does.Contain("Rumor"));
        Assert.That(prompt, Does.Contain("Disputed"));
        Assert.That(prompt, Does.Contain("Hidden"));
        Assert.That(prompt, Does.Contain("Truth State"));
    }

    [Test]
    public void BuildSystemPrompt_PrivateSource_InstructsPrivateVisibility()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "JournalEntry",
            SourceVisibility = "Private"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Private"));
        Assert.That(prompt, Does.Contain("MUST include \"visibility\": \"Private\""));
    }

    [Test]
    public void BuildUserMessage_WithReferencePassages_IncludesPublishedReferenceSection()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "We questioned Captain Voss.",
            SourceTitle = "Session 5 Notes",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            ReferencePassages =
            [
                new KnowledgePassage
                {
                    ChunkId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    DocumentTitle = "Player's Handbook",
                    Page = 42,
                    Text = "A ranger is a warden of the wilds.",
                    ReferenceId = "passage:x"
                }
            ]
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain("## Published Reference"));
        Assert.That(message, Does.Contain("Player's Handbook"));
        Assert.That(message, Does.Contain("p. 42"));
        Assert.That(message, Does.Contain("A ranger is a warden of the wilds."));
    }

    [Test]
    public void BuildUserMessage_NoReferencePassages_OmitsPublishedReferenceSection()
    {
        var message = ExtractionPromptBuilder.BuildUserMessage(DefaultRequest);

        Assert.That(message, Does.Not.Contain("Published Reference"));
    }

    [Test]
    public void BuildUserMessage_WithRecentLocations_IncludesLocationContextSection()
    {
        var harborId = Guid.NewGuid();
        var request = new ExtractionRequest
        {
            SourceBody = "We went back to the tavern.",
            SourceTitle = "Session 5 Notes",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            RecentLocations = new RecentLocationContext
            {
                SourceTitle = "Session 4 Notes",
                OccurredAt = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
                Locations =
                [
                    new PriorLocation { Id = harborId, Name = "Black Harbor", Summary = "A smuggler port." },
                    new PriorLocation { Id = Guid.NewGuid(), Name = "The Iron Gate" }
                ]
            }
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain("## Location Context"));
        Assert.That(message, Does.Contain("Session 4 Notes"));
        Assert.That(message, Does.Contain("(2026-07-10)"));
        Assert.That(message, Does.Contain($"- Black Harbor (Id: {harborId}) — A smuggler port."));
        Assert.That(message, Does.Contain("- The Iron Gate (Id: "));
        // The hint precedes the source so the model reads it as framing, not content.
        Assert.That(message.IndexOf("## Location Context", StringComparison.Ordinal),
            Is.LessThan(message.IndexOf("## Source Content", StringComparison.Ordinal)));
    }

    [Test]
    public void BuildUserMessage_NoRecentLocations_OmitsLocationContextSection()
    {
        var message = ExtractionPromptBuilder.BuildUserMessage(DefaultRequest);

        Assert.That(message, Does.Not.Contain("Location Context"));
    }

    [Test]
    public void BuildSystemPrompt_WithRecentLocations_TeachesCarriedForwardRules()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "We went back to the tavern.",
            SourceTitle = "Session 5 Notes",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            RecentLocations = new RecentLocationContext
            {
                SourceTitle = "Session 4 Notes",
                Locations = [new PriorLocation { Id = Guid.NewGuid(), Name = "Black Harbor" }]
            }
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("## Location Context"));
        Assert.That(prompt, Does.Contain("the source always wins"));
        Assert.That(prompt, Does.Contain("whose only support is the Location"));
    }

    [Test]
    public void BuildSystemPrompt_NoRecentLocations_OmitsLocationContextSection()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.That(prompt, Does.Not.Contain("## Location Context"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesPublishedReferenceMaterialClause()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.That(prompt, Does.Contain("Published Reference Material"));
        Assert.That(prompt, Does.Contain("NOT world canon"));
    }

    [Test]
    public void BuildSystemPrompt_TeachesEventStorylineLinks()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("also propose AddRelationship linking the Event to that Storyline"));
        Assert.That(prompt, Does.Contain("\"Advances\""));
    }

    [Test]
    public void BuildSystemPrompt_TeachesStorylineHierarchy()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Storyline Hierarchy"));
        Assert.That(prompt, Does.Contain("\"PartOf\""));
        Assert.That(prompt, Does.Contain("Never propose PartOf for a"));
        Assert.That(prompt, Does.Contain("already shows \"Part of\""));
    }

    [Test]
    public void BuildUserMessage_NestedStoryline_ShowsPartOfLine()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            ExistingArtifacts =
            [
                new ArtifactContext
                {
                    Id = Guid.NewGuid(),
                    Name = "Kastor Watch Investigation",
                    Type = "Storyline",
                    Summary = "The watch digs in.",
                    PartOfName = "Kastor Crisis"
                }
            ]
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain("Part of: Kastor Crisis"));
    }

    #region Feature 43 — significance, storylines, retired storylines

    [Test]
    public void BuildSystemPrompt_DefinesSignificance_AndSaysIncidentalIsDiscarded()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("## Significance"));
            Assert.That(prompt, Does.Contain("\"Core\""));
            Assert.That(prompt, Does.Contain("\"Supporting\""));
            Assert.That(prompt, Does.Contain("\"Incidental\""));
            // The raw string literal strips the source indentation, so assertions stay on one line.
            Assert.That(prompt, Does.Contain("Incidental proposals are discarded before anyone sees them"));
            Assert.That(prompt, Does.Contain("texture may produce nothing, and that is a correct reading"));
            Assert.That(prompt, Does.Contain("for it is at least Supporting"));
            Assert.That(prompt, Does.Contain("significance (\"Core\", \"Supporting\" or \"Incidental\""));
        });
    }

    [Test]
    public void BuildSystemPrompt_DefinesAStorylineByIntentAndResolution_WithTheWitchAsTheCounterExample()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("## Storylines"));
            Assert.That(prompt, Does.Contain("intends something, and there is a conceivable moment at which it resolves"));
            Assert.That(prompt, Does.Contain("Both halves are"));
            Assert.That(prompt, Does.Contain("a witch warning that someone is"));
            Assert.That(prompt, Does.Contain("never an arc of its own"));
            Assert.That(prompt, Does.Contain("never propose more than two"));
            Assert.That(prompt, Does.Contain("Prefer advancing an existing Storyline to creating a new one"));
        });
    }

    [Test]
    public void BuildSystemPrompt_NoLongerSaysEveryEventBelongsToAnArc()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Not.Contain("almost always belongs to an arc"));
            Assert.That(prompt, Does.Not.Contain("first-class"));
            Assert.That(prompt, Does.Contain("arc behind it is fine left unlinked"));
        });
    }

    [Test]
    public void BuildSystemPrompt_OpenQuestions_NeedThePartyPursuingThem()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("When the party is pursuing a question the record cannot yet answer"));
            Assert.That(prompt, Does.Contain("nobody has taken up is not an open question"));
        });
    }

    [Test]
    public void BuildSystemPrompt_ExplainsRetiredStorylines()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("## Retired Storylines"));
            Assert.That(prompt, Does.Contain("\"Status: Dormant\" or \"Status: Resolved\""));
            Assert.That(prompt, Does.Contain("Never propose a new Storyline about the same matter"));
            Assert.That(prompt, Does.Contain("propose UpdateArtifact on it setting status \"Active\""));
        });
    }

    [Test]
    public void BuildSystemPrompt_ImportedNotes_LinkedTermsAreAtLeastSupporting()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "[[Captain Voss]] was seen.",
            SourceTitle = "Old note",
            SourceType = "ImportedNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("Supporting rather than Incidental: the user already decided it matters"));
            Assert.That(prompt, Does.Not.Contain("dropped as incidental detail"));
        });
    }

    [TestCase("Dormant")]
    [TestCase("Resolved")]
    public void BuildUserMessage_RetiredArtifact_ShowsStatusLine(string status)
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            ExistingArtifacts =
            [
                new ArtifactContext
                {
                    Id = Guid.NewGuid(),
                    Name = "Missing Caravan",
                    Type = "Storyline",
                    Status = status
                }
            ]
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain($"Status: {status}"));
        Assert.That(ExtractionPromptReader.Parse(message).ExistingArtifacts.Single().Status, Is.EqualTo(status));
    }

    [TestCase("Active")]
    [TestCase(null)]
    public void BuildUserMessage_ActiveOrUnknownStatus_ShowsNoStatusLine(string? status)
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            ExistingArtifacts =
            [
                new ArtifactContext
                {
                    Id = Guid.NewGuid(),
                    Name = "Missing Caravan",
                    Type = "Storyline",
                    Status = status
                }
            ]
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Not.Contain("Status: "));
        Assert.That(ExtractionPromptReader.Parse(message).ExistingArtifacts.Single().Status, Is.Null);
    }

    #endregion

    [Test]
    public void BuildSystemPrompt_IncludesLiterarySourceInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Literary and Authored Sources"));
        Assert.That(prompt, Does.Contain("Document artifact for the work itself"));
        Assert.That(prompt, Does.Contain("at best Likely, never Confirmed"));
        Assert.That(prompt, Does.Contain("Still extract the real artifacts the work establishes"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesRationaleInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("rationale"));
        Assert.That(prompt, Does.Contain("max 500 characters"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesOpenQuestionConvention()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("open question"));
        Assert.That(prompt, Does.Contain("re-propose an open question that already exists"));
    }

    #endregion

    #region User Message Tests

    [Test]
    public void BuildUserMessage_IncludesSourceFields()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Captain Voss was seen at the docks.",
            SourceTitle = "Session 7 Notes",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            OccurredAt = new DateTimeOffset(2024, 3, 15, 20, 0, 0, TimeSpan.Zero)
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain("Session 7 Notes"));
        Assert.That(message, Does.Contain("SessionNote"));
        Assert.That(message, Does.Contain("PartyVisible"));
        Assert.That(message, Does.Contain("Captain Voss was seen at the docks."));
        Assert.That(message, Does.Contain("2024-03-15"));
    }

    [Test]
    public void BuildSystemPrompt_ImportedNote_IncludesImportedNotesInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Heading to [[Kastor]]",
            SourceTitle = "2024-01-24",
            SourceType = "ImportedNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("## Imported Notes"));
        Assert.That(prompt, Does.Contain("[[double brackets]]"));
        Assert.That(prompt, Does.Contain("{curly braces}"));
    }

    [Test]
    public void BuildSystemPrompt_LibraryExcerpt_ReadsAsWrittenCanon_ConfirmedByDefault()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Excerpt from “Player's Guide”, pp. 42–45, filed for the codex entry “Thistlehold”.\n\nThistlehold sits on the river.",
            SourceTitle = "Thistlehold — Player's Guide, pp. 42–45",
            SourceType = "LibraryExcerpt",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("## Library Excerpt"));
        Assert.That(prompt, Does.Contain("filing line"));
        Assert.That(prompt, Does.Contain("rate them \"Confirmed\", not \"Likely\""));
        Assert.That(prompt, Does.Contain("rather than proposing a new"));
        Assert.That(prompt, Does.Contain("artifact of the same name"));
    }

    [Test]
    public void BuildSystemPrompt_AdventureExport_IsAuthoritative_HiddenForSecrets_AndNoEventsFromPlans()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Adventure \"The Silver Key\", exported 2026-11-14 by the GM as prep\n\n## Captain Voss\n\nVoss secretly serves the Shadow Guild.",
            SourceTitle = "The Silver Key (prep)",
            SourceType = "AdventureExport",
            SourceVisibility = "GMOnly"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("## Adventure Prep"));
            Assert.That(prompt, Does.Contain("authoritative about the world"));
            Assert.That(prompt, Does.Contain("as \"Hidden\""));
            Assert.That(prompt, Does.Contain("Never propose the"));
            Assert.That(prompt, Does.Contain("as Event artifacts"));
            Assert.That(prompt, Does.Contain("\"Confirmed\", as for an excerpt"));
            Assert.That(prompt, Does.Not.Contain("## Library Excerpt"));
        });
    }

    /// <summary>
    /// Feature 35: a named block's export line is one fact, and the prompt names the line by the
    /// very start <see cref="GameBlocks.ExportLineFor"/> writes, so the two can't drift apart.
    /// </summary>
    [Test]
    public void BuildSystemPrompt_AdventureExport_FilesABlockLine_AsOneFact_ByTheLinesOwnStart()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(new ExtractionRequest
        {
            SourceBody = "Adventure \"A\", exported 2026-11-14 by the GM as prep",
            SourceTitle = "A (prep)",
            SourceType = "AdventureExport",
            SourceVisibility = "GMOnly"
        });
        var stat = GameBlocks.ExportLineFor(GameBlocks.Parse(GameBlockKind.Stat, ["# Name", "AC: 1"]), "OSE")!.Text;
        var item = GameBlocks.ExportLineFor(GameBlocks.Parse(GameBlockKind.Item, ["# Name", "Weight: 1"]), "OSE")!.Text;

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain($"`{stat[..(stat.IndexOf("Name", StringComparison.Ordinal) + 5)]}`"));
            Assert.That(prompt, Does.Contain($"`{item[..(item.IndexOf("Name", StringComparison.Ordinal) + 5)]}`"));
            Assert.That(prompt, Does.Contain("Use the predicate \"stat block\" or \"item"));
            Assert.That(prompt, Does.Contain("Don't split it into"));
        });
    }

    [Test]
    public void BuildSystemPrompt_SessionNote_OmitsAdventurePrepInstructions()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.That(prompt, Does.Not.Contain("## Adventure Prep"));
    }

    [Test]
    public void BuildSystemPrompt_SessionNote_OmitsLibraryExcerptInstructions()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(DefaultRequest);

        Assert.That(prompt, Does.Not.Contain("## Library Excerpt"));
    }

    [Test]
    public void BuildSystemPrompt_NonImportedNote_OmitsImportedNotesInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Not.Contain("## Imported Notes"));
    }

    [Test]
    public void BuildUserMessage_WithCampaign_IncludesCampaignContext()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Some content",
            SourceTitle = "Test Note",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            CampaignName = "Rise of Tiamat",
            CampaignStatus = "Active"
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain("Campaign: Rise of Tiamat (Active)"));
    }

    [Test]
    public void BuildUserMessage_NoCampaign_OmitsCampaignContext()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Some content",
            SourceTitle = "Test Note",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            CampaignName = null
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Not.Contain("Campaign:"));
    }

    [Test]
    public void BuildUserMessage_NullOccurredAt_OmitsTemporalContext()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Some content",
            SourceTitle = "Test Note",
            SourceType = "GMNote",
            SourceVisibility = "GMOnly",
            OccurredAt = null
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Not.Contain("Occurred At"));
    }

    [Test]
    public void BuildUserMessage_WithExistingArtifacts_IncludesArtifactContext()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Some content",
            SourceTitle = "Test Note",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            ExistingArtifacts =
            [
                new ArtifactContext
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Captain Voss",
                    Type = "Character",
                    Summary = "A shady harbor captain.",
                    Facts =
                    [
                        new FactContext { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Predicate = "location", Value = "Black Harbor" },
                        new FactContext { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Predicate = "occupation", Value = "Ship captain" }
                    ]
                }
            ]
        };

        var message = ExtractionPromptBuilder.BuildUserMessage(request);

        Assert.That(message, Does.Contain("Captain Voss"));
        Assert.That(message, Does.Contain("11111111-1111-1111-1111-111111111111"));
        Assert.That(message, Does.Contain("Character"));
        Assert.That(message, Does.Contain("A shady harbor captain."));
        Assert.That(message, Does.Contain("location: Black Harbor"));
        Assert.That(message, Does.Contain("occupation: Ship captain"));
        Assert.That(message, Does.Contain("[factId: 22222222-2222-2222-2222-222222222222]"),
            "fact ids must reach the model — UpdateFact targeting depends on them");
    }

    #endregion
}
