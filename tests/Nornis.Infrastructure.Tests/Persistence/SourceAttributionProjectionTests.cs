using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The artifact detail page shows which sources cite each fact. It used to read a whole
/// <c>Source</c> row per citation — transcript and all — to get a title, once per source, on the
/// most-visited authenticated page (also served anonymously). This projection replaces that.
///
/// It feeds a visibility decision, so the fields it carries are load-bearing: drop
/// <c>CreatedByUserId</c> and a Private note stops being visible to its own author; drop
/// <c>Visibility</c> and everything leaks.
/// </summary>
[TestFixture]
public class SourceAttributionProjectionTests : IntegrationTestBase
{
    private SourceRepository _repository = null!;
    private Guid _worldId;
    private Guid _authorId;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _authorId = Guid.NewGuid();
        var tag = Guid.NewGuid().ToString("N");

        Context.Sources.RemoveRange(Context.Sources);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.Add(new User
        {
            Id = _authorId,
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            CreatedByUserId = _authorId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        _repository = new SourceRepository(Context);
    }

    private async Task<Guid> SeedSourceAsync(string title, VisibilityScope visibility, string body)
    {
        var id = Guid.NewGuid();
        Context.Sources.Add(new Source
        {
            Id = id,
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = title,
            Body = body,
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _authorId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task CarriesEveryFieldTheVisibilityDecisionNeeds()
    {
        var id = await SeedSourceAsync("Session 4", VisibilityScope.GMOnly, "secret notes");

        var result = await _repository.ListAttributionByIdsAsync([id]);

        var only = result.Single();
        Assert.Multiple(() =>
        {
            Assert.That(only.Id, Is.EqualTo(id));
            Assert.That(only.Title, Is.EqualTo("Session 4"));
            Assert.That(only.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(only.CreatedByUserId, Is.EqualTo(_authorId));
        });
    }

    [Test]
    public async Task ReturnsOneRowPerRequestedId_InOneQuery()
    {
        var a = await SeedSourceAsync("A", VisibilityScope.PartyVisible, "a");
        var b = await SeedSourceAsync("B", VisibilityScope.Private, "b");
        var c = await SeedSourceAsync("C", VisibilityScope.GMOnly, "c");

        var result = await _repository.ListAttributionByIdsAsync([a, b, c]);

        Assert.That(result.Select(r => r.Title), Is.EquivalentTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public async Task UnknownIds_AreAbsentRatherThanNull()
    {
        // Callers fail closed on a reference they cannot attribute, so a missing row must simply
        // not appear — not arrive as a null-titled placeholder that renders as an empty citation.
        var known = await SeedSourceAsync("Known", VisibilityScope.PartyVisible, "x");

        var result = await _repository.ListAttributionByIdsAsync([known, Guid.NewGuid()]);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.Single().Id, Is.EqualTo(known));
    }

    [Test]
    public async Task EmptyInput_DoesNotQuery()
    {
        var result = await _repository.ListAttributionByIdsAsync([]);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task DoesNotProjectTheSourceBody()
    {
        // The whole point: SourceAttribution has no Body/DerivedText member, so a citation list
        // can never drag transcripts across the wire. Pinned as a type-level guarantee, because
        // adding one later would silently reintroduce the original cost.
        await SeedSourceAsync("Long note", VisibilityScope.PartyVisible, new string('x', 50_000));

        var properties = typeof(Domain.Models.SourceAttribution)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.That(properties, Is.EquivalentTo(new[]
        {
            nameof(Domain.Models.SourceAttribution.Id),
            nameof(Domain.Models.SourceAttribution.Title),
            nameof(Domain.Models.SourceAttribution.Visibility),
            nameof(Domain.Models.SourceAttribution.CreatedByUserId),
        }));
    }
}
