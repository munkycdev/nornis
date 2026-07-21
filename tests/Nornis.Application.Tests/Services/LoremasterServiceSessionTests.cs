using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Temporal grounding for the Loremaster: recent sessions in the prompt, provenance
/// date stamps on retrieved knowledge, and a "today" anchor — so "what happened last
/// session?" can no longer be answered from a months-old session.
/// </summary>
[TestFixture]
public class LoremasterServiceSessionTests
{
    private LoremasterService _service = null!;
    private FakeKnowledgeRetriever _knowledgeRetriever = null!;
    private FakeReferencePassageRetriever _passageRetriever = null!;
    private FakeLoremasterAiClient _aiClient = null!;

    [SetUp]
    public void SetUp()
    {
        _knowledgeRetriever = new FakeKnowledgeRetriever();
        _passageRetriever = new FakeReferencePassageRetriever();
        _aiClient = new FakeLoremasterAiClient();
        _service = new LoremasterService(
            _knowledgeRetriever,
            _passageRetriever,
            _aiClient,
            new InMemoryAiUsageRecordRepository(),
            new FakeAiBudgetGuard(),
            Options.Create(new LoremasterOptions
            {
                AiModel = "gpt-4o",
                AiTimeoutSeconds = 30,
                MaxRetrievalCount = 30,
                MaxQuestionLength = 2000
            }));
    }

    private static KnowledgeContext EmptyContext(IReadOnlyList<KnowledgeSession>? sessions = null) => new()
    {
        Artifacts = [],
        Facts = [],
        Relationships = [],
        SourceReferences = [],
        Sessions = sessions ?? []
    };

    private static KnowledgeSession MakeSession(
        string title, DateTimeOffset date, string? text = "The party did things.") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Date = date,
        Text = text,
        ReferenceId = $"session:{Guid.NewGuid()}"
    };

    // ---------------------------------------------------------------- Prompt content --

    [Test]
    public void SystemPromptTemplate_ContainsTemporalityRules()
    {
        Assert.That(LoremasterService.SystemPromptTemplate, Does.Contain("## Temporality"));
        Assert.That(LoremasterService.SystemPromptTemplate, Does.Contain("Recent Sessions"));
        Assert.That(LoremasterService.SystemPromptTemplate,
            Does.Contain("do not substitute older material"));
    }

    [Test]
    public void BuildPrompt_AnchorsTodaysDate()
    {
        var request = _service.BuildPrompt("What happened?", EmptyContext());

        Assert.That(request.UserMessage,
            Does.Contain($"Today's date: {DateTimeOffset.UtcNow:yyyy-MM-dd}"));
    }

    [Test]
    public void FormatKnowledgeContext_RendersSessionsNewestFirst_WithDatesAndMarker()
    {
        var context = EmptyContext(
        [
            MakeSession("The Siege", new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero)),
            MakeSession("The Road South", new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero))
        ]);

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("### Recent Sessions (most recent first)"));
        Assert.That(formatted, Does.Contain("\"The Siege\" — played 2026-07-14 — this is the most recent session"));
        Assert.That(formatted, Does.Contain("\"The Road South\" — played 2026-06-30"));
        // Only the newest session carries the marker.
        Assert.That(formatted.IndexOf("most recent session", StringComparison.Ordinal),
            Is.EqualTo(formatted.LastIndexOf("most recent session", StringComparison.Ordinal)));
    }

    [Test]
    public void FormatKnowledgeContext_IncludesFullSessionText_Untruncated()
    {
        var longText = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Event {i} unfolded."));
        var context = EmptyContext([MakeSession("Long night", DateTimeOffset.UtcNow, longText)]);

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("Event 0 unfolded."));
        Assert.That(formatted, Does.Contain("Event 199 unfolded."));
    }

    [Test]
    public void FormatKnowledgeContext_MarksSessionsWithoutText()
    {
        var context = EmptyContext([MakeSession("Silent session", DateTimeOffset.UtcNow, text: null)]);

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("(no written record for this session)"));
    }

    [Test]
    public void FormatKnowledgeContext_StampsFactProvenance_WithNewestMention()
    {
        var factId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts =
            [
                new KnowledgeArtifact
                {
                    Id = artifactId, Name = "Captain Voss", Type = "Character",
                    Summary = "A captain", Status = "Active", ReferenceId = $"artifact:{artifactId}"
                }
            ],
            Facts =
            [
                new KnowledgeFact
                {
                    Id = factId, ArtifactId = artifactId, Predicate = "location",
                    Value = "Black Harbor", TruthState = TruthState.Confirmed,
                    ReferenceId = $"fact:{factId}"
                }
            ],
            Relationships = [],
            SourceReferences =
            [
                new KnowledgeSourceReference
                {
                    Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), TargetId = factId,
                    Quote = "old mention", ReferenceId = "src:old",
                    SourceTitle = "Session 3", SourceDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)
                },
                new KnowledgeSourceReference
                {
                    Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), TargetId = factId,
                    Quote = "new mention", ReferenceId = "src:new",
                    SourceTitle = "Session 12", SourceDate = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        // The fact is dated by its newest mention, and quotes name their origin.
        Assert.That(formatted, Does.Contain("(recorded in \"Session 12\", 2026-07-01)"));
        Assert.That(formatted, Does.Contain("\"old mention\" (from \"Session 3\", 2026-02-01)"));
    }

    [Test]
    public void FormatKnowledgeContext_OmitsProvenance_WhenSourceUnloaded()
    {
        var factId = Guid.NewGuid();
        var context = new KnowledgeContext
        {
            Artifacts = [],
            Facts = [],
            Relationships = [],
            SourceReferences =
            [
                new KnowledgeSourceReference
                {
                    Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), TargetId = factId,
                    Quote = "a quote", ReferenceId = "src:x"
                }
            ]
        };

        var formatted = LoremasterService.FormatKnowledgeContext(context);

        Assert.That(formatted, Does.Contain("\"a quote\" [ref:src:x]"));
        Assert.That(formatted, Does.Not.Contain("(from"));
    }

    // ---------------------------------------------------------------- Passage merge --

    [Test]
    public async Task AskAsync_SessionsSurviveThePassageMerge()
    {
        // Regression: when library passages come back, AskAsync rebuilds the context —
        // sessions were dropped in the copy, so worlds with indexed rulebooks (i.e. any
        // world using the Library) silently lost the Recent Sessions section.
        _knowledgeRetriever.NextContext = EmptyContext([MakeSession("The Siege", DateTimeOffset.UtcNow)]);
        _passageRetriever.Passages.Add(new KnowledgePassage
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentTitle = "Core Rules",
            Page = 12,
            Text = "A rule.",
            ReferenceId = $"passage:{Guid.NewGuid()}",
        });

        var result = await _service.AskAsync(
            new AskLoremasterCommand(Guid.NewGuid(), "What happened last session?", Guid.NewGuid(), WorldRole.GM, null),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_aiClient.LastRequest!.UserMessage, Does.Contain("### Recent Sessions"));
        Assert.That(_aiClient.LastRequest.UserMessage, Does.Contain("The Siege"));
        Assert.That(_aiClient.LastRequest.UserMessage, Does.Contain("### Published Reference"));
    }

    // -------------------------------------------------------------------- Citations --

    [Test]
    public void ParseCitations_ResolvesSessionReferences_AsSourceCitations()
    {
        var session = MakeSession("The Siege", DateTimeOffset.UtcNow);
        var context = EmptyContext([session]);

        var citations = LoremasterService.ParseCitations(
            $"The gate fell [ref:{session.ReferenceId}].", context);

        Assert.That(citations, Has.Count.EqualTo(1));
        Assert.That(citations[0].Type, Is.EqualTo(CitationType.Source));
        Assert.That(citations[0].DisplayName, Is.EqualTo("The Siege"));
        Assert.That(citations[0].SourceId, Is.EqualTo(session.Id));
    }

    // ---------------------------------------------------------- Confidence & caveats --

    [Test]
    public void DetermineConfidence_SessionsWithText_NoArtifacts_IsMedium()
    {
        var context = EmptyContext([MakeSession("A session", DateTimeOffset.UtcNow)]);

        Assert.That(LoremasterService.DetermineConfidence(context), Is.EqualTo(ConfidenceLevel.Medium));
    }

    [Test]
    public void DetermineConfidence_SessionsWithoutText_NoArtifacts_IsLow()
    {
        var context = EmptyContext([MakeSession("A session", DateTimeOffset.UtcNow, text: null)]);

        Assert.That(LoremasterService.DetermineConfidence(context), Is.EqualTo(ConfidenceLevel.Low));
    }

    [Test]
    public void AssembleCaveats_SessionsPresent_DoesNotClaimLimitedInformation()
    {
        var context = EmptyContext([MakeSession("A session", DateTimeOffset.UtcNow)]);

        Assert.That(LoremasterService.AssembleCaveats(context),
            Does.Not.Contain("Limited information available"));
    }
}
