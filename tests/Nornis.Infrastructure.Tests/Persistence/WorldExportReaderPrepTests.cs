using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The Prep category of a world export (feature 29, Requirement 9.2): it carries this world's
/// Adventures with their sections and revisions, and nothing of another world's.
/// </summary>
[TestFixture]
public class WorldExportReaderPrepTests : IntegrationTestBase
{
    private WorldExportReader _sut = null!;
    private DateTimeOffset _now;

    [SetUp]
    public void SetUp()
    {
        _sut = new WorldExportReader(Context);
        _now = DateTimeOffset.UtcNow;
    }

    private async Task<(World World, Adventure Adventure)> SeedWorldAsync(string name)
    {
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        var world = new World { Id = Guid.NewGuid(), Name = name, CreatedByUserId = user.Id, CreatedAt = _now, UpdatedAt = _now };
        var adventure = new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Title = $"{name} heist",
            CreatedByUserId = user.Id,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        var section = new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = AdventureSectionKind.Scene,
            Title = "Scene 1",
            BodyMarkdown = $"{name} docks",
            SortOrder = 1,
            UpdatedAt = _now,
        };
        var revision = new AdventureSectionRevision
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            BodyMarkdown = "",
            Model = "gpt-test",
            CreatedAt = _now,
        };
        Context.AddRange(user, world, adventure, section, revision);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return (world, adventure);
    }

    [Test]
    public async Task Prep_LoadsThisWorldsAdventuresSectionsAndRevisions_Only()
    {
        var (mine, adventure) = await SeedWorldAsync("Black Harbor");
        await SeedWorldAsync("Elsewhere");

        var data = await _sut.ReadAsync(mine.Id, new HashSet<WorldExportCategory> { WorldExportCategory.Prep });

        Assert.That(data.Adventures.Select(a => a.Id), Is.EqualTo(new[] { adventure.Id }));
        Assert.That(data.AdventureSections.Select(s => s.BodyMarkdown), Is.EqualTo(new[] { "Black Harbor docks" }));
        Assert.That(data.AdventureSectionRevisions, Has.Count.EqualTo(1));
        Assert.That(data.AdventureSectionRevisions[0].SectionId, Is.EqualTo(data.AdventureSections[0].Id));
    }

    [Test]
    public async Task OtherCategories_LoadNoPrep()
    {
        var (mine, _) = await SeedWorldAsync("Black Harbor");

        var data = await _sut.ReadAsync(mine.Id, new HashSet<WorldExportCategory> { WorldExportCategory.Sources });

        Assert.That(data.Adventures, Is.Empty);
        Assert.That(data.AdventureSections, Is.Empty);
        Assert.That(data.AdventureSectionRevisions, Is.Empty);
    }
}
