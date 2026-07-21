using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Knowledge;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Knowledge;

/// <summary>
/// The retriever's session lane: the world's most recent play sessions ride along in the
/// knowledge context (newest first, by OccurredAt ?? CreatedAt) so time-anchored questions
/// ("what happened last session?") ground in the actual latest sessions — the bug this
/// guards against was the Loremaster confidently narrating a months-old session.
/// </summary>
[TestFixture]
public class RecentSessionRetrievalTests
{
    private Guid _worldId;
    private InMemorySourceRepository _sourceRepo = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _sourceRepo = new InMemorySourceRepository();
    }

    private KeywordKnowledgeRetriever CreateRetriever(int recentSessionCount = 3) => new(
        new InMemoryArtifactRepository(),
        new InMemoryArtifactFactRepository(),
        new InMemoryArtifactRelationshipRepository(),
        new InMemorySourceReferenceRepository(),
        _sourceRepo,
        Options.Create(new LoremasterOptions
        {
            MaxRetrievalCount = 30,
            MaxFactsPerArtifact = 15,
            RecentSessionCount = recentSessionCount
        }));

    private Source MakeSession(
        string title,
        DateTimeOffset? occurredAt,
        DateTimeOffset createdAt,
        SourceType type = SourceType.SessionNote,
        VisibilityScope visibility = VisibilityScope.PartyVisible,
        string? body = "Things happened.",
        string? derivedText = null) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _worldId,
        Type = type,
        Title = title,
        Body = body,
        DerivedText = derivedText,
        OccurredAt = occurredAt,
        CreatedAt = createdAt,
        CreatedByUserId = Guid.NewGuid(),
        Visibility = visibility,
        ProcessingStatus = SourceProcessingStatus.Processed
    };

    [Test]
    public async Task Sessions_AreOrderedNewestFirst_ByOccurredAtFallingBackToCreatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        // Undated session created most recently — CreatedAt stands in for OccurredAt,
        // so it must sort first despite the others carrying explicit dates.
        _sourceRepo.Seed(
            MakeSession("Old session", occurredAt: now.AddDays(-60), createdAt: now.AddDays(-60)),
            MakeSession("Undated but new", occurredAt: null, createdAt: now.AddDays(-1)),
            MakeSession("Mid session", occurredAt: now.AddDays(-10), createdAt: now.AddDays(-9)));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(context.Sessions.Select(s => s.Title),
            Is.EqualTo(new[] { "Undated but new", "Mid session", "Old session" }));
        Assert.That(context.Sessions[0].Date, Is.EqualTo(now.AddDays(-1)));
    }

    [Test]
    public async Task Sessions_AreCappedAtRecentSessionCount()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            _sourceRepo.Seed(MakeSession($"Session {i}", now.AddDays(-i), now.AddDays(-i)));
        }

        var context = await CreateRetriever(recentSessionCount: 2).RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(context.Sessions, Has.Count.EqualTo(2));
        Assert.That(context.Sessions.Select(s => s.Title), Is.EqualTo(new[] { "Session 0", "Session 1" }));
    }

    [Test]
    public async Task NonSessionSourceTypes_AreExcluded()
    {
        var now = DateTimeOffset.UtcNow;
        _sourceRepo.Seed(
            MakeSession("GM prep", now, now, type: SourceType.GMNote),
            MakeSession("A map", now, now, type: SourceType.Map),
            MakeSession("Lore upload", now, now, type: SourceType.Upload),
            MakeSession("Actual session", now.AddDays(-3), now.AddDays(-3)));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(context.Sessions.Select(s => s.Title), Is.EqualTo(new[] { "Actual session" }));
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task GmOnlySessions_AreHiddenFromNonGmAskers(WorldRole role)
    {
        var now = DateTimeOffset.UtcNow;
        _sourceRepo.Seed(
            MakeSession("Secret session", now, now, visibility: VisibilityScope.GMOnly),
            MakeSession("Party session", now.AddDays(-7), now.AddDays(-7)));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(context.Sessions.Select(s => s.Title), Is.EqualTo(new[] { "Party session" }));
    }

    [Test]
    public async Task GmSeesGmOnlySessions()
    {
        var now = DateTimeOffset.UtcNow;
        _sourceRepo.Seed(MakeSession("Secret session", now, now, visibility: VisibilityScope.GMOnly));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);

        Assert.That(context.Sessions.Select(s => s.Title), Is.EqualTo(new[] { "Secret session" }));
    }

    [Test]
    public async Task DatedImportedNotes_CountAsSessions_UndatedOnesDoNot()
    {
        // The bulk importer types everything ImportedNote; only session folders get an
        // OccurredAt. Dated = session record, undated = wiki/lore.
        var now = DateTimeOffset.UtcNow;
        _sourceRepo.Seed(
            MakeSession("Imported session", occurredAt: now.AddDays(-30), createdAt: now,
                type: SourceType.ImportedNote),
            MakeSession("Imported wiki page", occurredAt: null, createdAt: now,
                type: SourceType.ImportedNote));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(context.Sessions.Select(s => s.Title), Is.EqualTo(new[] { "Imported session" }));
    }

    [Test]
    public async Task Sessions_AreReturnedEvenWhenNoArtifactsMatch()
    {
        // A young world: session notes captured, no canon extracted yet.
        var now = DateTimeOffset.UtcNow;
        _sourceRepo.Seed(MakeSession("First session", now, now));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(context.Artifacts, Is.Empty);
        Assert.That(context.Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SessionText_FallsBackToDerivedText_WhenBodyIsEmpty()
    {
        var now = DateTimeOffset.UtcNow;
        _sourceRepo.Seed(
            MakeSession("Audio session", now, now, type: SourceType.SessionAudio,
                body: null, derivedText: "Transcribed audio."));

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(context.Sessions[0].Text, Is.EqualTo("Transcribed audio."));
    }

    [Test]
    public async Task SessionReferenceIds_UseSessionPrefix()
    {
        var now = DateTimeOffset.UtcNow;
        var session = MakeSession("A session", now, now);
        _sourceRepo.Seed(session);

        var context = await CreateRetriever().RetrieveAsync(
            "what happened last session", _worldId, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(context.Sessions[0].ReferenceId, Is.EqualTo($"session:{session.Id}"));
    }
}
