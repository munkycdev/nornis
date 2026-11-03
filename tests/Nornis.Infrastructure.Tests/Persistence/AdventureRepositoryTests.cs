using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Prep's repository against SQLite with foreign keys enforced. Every FK into or out of the
/// Prep tables is Restrict except the aggregate's own cascades, so a missing detach shows up
/// here as a constraint violation, which is how it would show up in production.
/// </summary>
[TestFixture]
public class AdventureRepositoryTests : IntegrationTestBase
{
    private AdventureRepository _sut = null!;
    private User _user = null!;
    private World _world = null!;
    private Campaign _campaign = null!;
    private LibraryDocument _print = null!;
    private DateTimeOffset _now;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new AdventureRepository(Context);
        _now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");

        _user = new User
        {
            Id = Guid.NewGuid(),
            SignIns = { new UserSignIn { Subject = $"auth0|{tag}" } },
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        _world = new World { Id = Guid.NewGuid(), Name = "Black Harbor", CreatedByUserId = _user.Id, CreatedAt = _now, UpdatedAt = _now };
        _campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Name = "Missing Caravan Arc",
            CreatedByUserId = _user.Id,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        _print = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Title = "The Silver Key, printed 2026-11-02",
            FileName = "silver-key.pdf",
            ContentType = "application/pdf",
            BlobPath = $"worlds/{_world.Id}/library/silver-key.pdf",
            Kind = LibraryDocumentKind.Handout,
            Visibility = VisibilityScope.GMOnly,
            Status = LibraryDocumentStatus.Indexed,
            UploadedByUserId = _user.Id,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        Context.AddRange(_user, _world, _campaign, _print);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<Adventure> CreateAsync(params string[] bodies)
    {
        var adventure = new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            CampaignId = _campaign.Id,
            Title = "The Silver Key",
            CreatedByUserId = _user.Id,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        adventure.Sections = bodies.Select((body, i) => new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = AdventureSectionKind.Notes,
            Title = $"Section {i + 1}",
            BodyMarkdown = body,
            SortOrder = i + 1,
            UpdatedAt = _now,
        }).ToList();

        await _sut.CreateAsync(adventure);
        Context.ChangeTracker.Clear();
        return adventure;
    }

    private async Task<AdventureSectionRevision> AddRevisionAsync(Guid sectionId, string body, DateTimeOffset at)
    {
        var revision = new AdventureSectionRevision
        {
            Id = Guid.NewGuid(),
            SectionId = sectionId,
            BodyMarkdown = body,
            Model = "gpt-5.4",
            CreatedAt = at,
        };
        Context.AdventureSectionRevisions.Add(revision);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return revision;
    }

    [Test]
    public async Task Create_InsertsTheSections_AndAssignsASlug()
    {
        var adventure = await CreateAsync("a", "b");

        var loaded = await _sut.GetWithSectionsAsync(adventure.Id);

        Assert.That(loaded!.Slug, Is.EqualTo("the-silver-key"));
        Assert.That(loaded.Sections.Select(s => s.BodyMarkdown), Is.EqualTo(["a", "b"]));
    }

    [Test]
    public async Task List_CountsFilledSections_ByTheBodiesTheCallerCallsUnfilled()
    {
        await CreateAsync("", "template", "written");

        var list = await _sut.ListByWorldAsync(_world.Id, ["", "template"]);


        Assert.That((list.Single().SectionCount, list.Single().FilledSectionCount), Is.EqualTo((3, 1)));
    }

    [Test]
    public async Task List_SaysWhichAdventuresASourceWasExportedFrom()
    {
        var exported = await CreateAsync("a");
        var untouched = await CreateAsync("b");
        Context.Sources.Add(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Type = SourceType.AdventureExport,
            Title = "The Silver Key (prep)",
            Body = "Adventure \"The Silver Key\", exported by the GM as prep",
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _user.Id,
            CreatedAt = _now,
            AdventureId = exported.Id,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var list = await _sut.ListByWorldAsync(_world.Id, [""]);

        Assert.Multiple(() =>
        {
            Assert.That(list.Single(a => a.Id == exported.Id).IsExported, Is.True);
            Assert.That(list.Single(a => a.Id == untouched.Id).IsExported, Is.False);
        });
    }

    [Test]
    public async Task Details_RoundTrip_AndNoneSet_ReadsBackEmpty_NotNull()
    {
        var adventure = await CreateAsync("a");

        var fresh = await _sut.GetWithSectionsAsync(adventure.Id);
        Assert.That(fresh!.Details, Is.EqualTo(new AdventureDetails()));

        fresh.Details = new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st", Levels = "1–3", Year = 2026 };
        await _sut.UpdateAsync(fresh);
        Context.ChangeTracker.Clear();

        var stored = await _sut.GetWithSectionsAsync(adventure.Id);
        Assert.That(stored!.Details, Is.EqualTo(fresh.Details));
    }

    [Test]
    public async Task Details_UpdatedTwiceInOneScope_Succeeds()
    {
        // The second LoadForUpdate would meet the first's owned entry if it stayed tracked.
        var adventure = await CreateAsync("a");
        var first = (await _sut.GetWithSectionsAsync(adventure.Id))!;
        first.Details = new AdventureDetails { Edition = "1st" };
        await _sut.UpdateAsync(first);

        var second = (await _sut.GetWithSectionsAsync(adventure.Id))!;
        second.Details = second.Details with { Year = 2026 };
        await _sut.UpdateAsync(second);
        Context.ChangeTracker.Clear();

        var stored = await _sut.GetWithSectionsAsync(adventure.Id);
        Assert.That(stored!.Details, Is.EqualTo(new AdventureDetails { Edition = "1st", Year = 2026 }));
    }

    [Test]
    public async Task PrintDetails_RoundTripOnAPrint_AndAreNullOnADocumentThatIsNot()
    {
        var details = PrintDetails.Resolve(
            "The Silver Key", new AdventureDetails { Edition = "1st", Year = 2026 }, "Shadowdark", _user.Username);
        var print = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Title = "The Silver Key, printed 2026-11-03",
            FileName = "silver-key-2.pdf",
            ContentType = "application/pdf",
            BlobPath = $"worlds/{_world.Id}/library/silver-key-2.pdf",
            Kind = LibraryDocumentKind.Handout,
            Visibility = VisibilityScope.GMOnly,
            Status = LibraryDocumentStatus.Indexed,
            PrintDetails = details,
            UploadedByUserId = _user.Id,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        Context.LibraryDocuments.Add(print);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var storedPrint = await Context.LibraryDocuments.AsNoTracking().SingleAsync(d => d.Id == print.Id);
        var storedUpload = await Context.LibraryDocuments.AsNoTracking().SingleAsync(d => d.Id == _print.Id);

        Assert.Multiple(() =>
        {
            Assert.That(storedPrint.PrintDetails, Is.EqualTo(details));
            Assert.That(storedUpload.PrintDetails, Is.Null);
        });
    }

    [Test]
    public async Task TryUpdateSection_BasedOnTheStoredStamp_Writes()
    {
        var adventure = await CreateAsync("old");
        var section = adventure.Sections.Single();
        var later = _now.AddSeconds(5);

        var written = await _sut.TryUpdateSectionAsync(
            new AdventureSection { Id = section.Id, Kind = AdventureSectionKind.Scene, Title = "Scene 1", BodyMarkdown = "new" },
            _now, later);

        var stored = await Context.AdventureSections.AsNoTracking().SingleAsync(s => s.Id == section.Id);
        Assert.That(written, Is.True);
        Assert.That((stored.BodyMarkdown, stored.Kind, stored.UpdatedAt), Is.EqualTo(("new", AdventureSectionKind.Scene, later)));
        Assert.That((await Context.Adventures.AsNoTracking().SingleAsync(a => a.Id == adventure.Id)).UpdatedAt, Is.EqualTo(later));
    }

    [Test]
    public async Task TryUpdateSection_BasedOnAStaleStamp_WritesNothing()
    {
        var adventure = await CreateAsync("old");
        var section = adventure.Sections.Single();

        var written = await _sut.TryUpdateSectionAsync(
            new AdventureSection { Id = section.Id, Kind = section.Kind, Title = section.Title, BodyMarkdown = "new" },
            _now.AddSeconds(-1), _now.AddSeconds(5));

        Assert.That(written, Is.False);
        Assert.That((await Context.AdventureSections.AsNoTracking().SingleAsync(s => s.Id == section.Id)).BodyMarkdown, Is.EqualTo("old"));
    }

    [Test]
    public async Task InsertSection_AfterAnother_Renumbers()
    {
        var adventure = await CreateAsync("a", "b");
        var inserted = new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = AdventureSectionKind.Notes,
            Title = "Between",
            UpdatedAt = _now,
        };

        await _sut.InsertSectionAsync(inserted, adventure.Sections.First().Id);

        var loaded = await _sut.GetWithSectionsAsync(adventure.Id);
        Assert.That(loaded!.Sections.Select(s => (s.Title, s.SortOrder)),
            Is.EqualTo([("Section 1", 1), ("Between", 2), ("Section 2", 3)]));
    }

    [Test]
    public async Task ReorderAndDeleteSection_KeepTheNumbering()
    {
        var adventure = await CreateAsync("a", "b", "c");
        var ids = adventure.Sections.Select(s => s.Id).ToList();
        await AddRevisionAsync(ids[1], "older b", _now);

        await _sut.ReorderSectionsAsync(adventure.Id, [ids[2], ids[1], ids[0]]);
        await _sut.DeleteSectionAsync(adventure.Id, ids[1]);

        var loaded = await _sut.GetWithSectionsAsync(adventure.Id);
        Assert.That(loaded!.Sections.Select(s => (s.Id, s.SortOrder)), Is.EqualTo([(ids[2], 1), (ids[0], 2)]));
        Assert.That(await Context.AdventureSectionRevisions.CountAsync(r => ids.Contains(r.SectionId)), Is.Zero);
    }

    [Test]
    public async Task TryRestoreRevision_WritesTheBody_AndDropsTheRevision_Together()
    {
        var adventure = await CreateAsync("drafted");
        var section = adventure.Sections.Single();
        await AddRevisionAsync(section.Id, "first", _now.AddMinutes(-2));
        var latest = await AddRevisionAsync(section.Id, "before the draft", _now.AddMinutes(-1));

        var found = await _sut.GetLatestRevisionAsync(section.Id);
        var restored = await _sut.TryRestoreRevisionAsync(found!, _now, _now.AddSeconds(1));

        Assert.That(found!.Id, Is.EqualTo(latest.Id));
        Assert.That(restored, Is.True);
        Assert.That((await Context.AdventureSections.AsNoTracking().SingleAsync(s => s.Id == section.Id)).BodyMarkdown, Is.EqualTo("before the draft"));
        Assert.That(await Context.AdventureSectionRevisions.Where(r => r.SectionId == section.Id).Select(r => r.BodyMarkdown).ToListAsync(),
            Is.EqualTo(["first"]));
        Assert.That((await _sut.CountRevisionsAsync(adventure.Id))[section.Id], Is.EqualTo(1));
    }

    [Test]
    public async Task TryRestoreRevision_OnAStaleStamp_WritesNothing()
    {
        var adventure = await CreateAsync("drafted");
        var section = adventure.Sections.Single();
        var revision = await AddRevisionAsync(section.Id, "before", _now);

        var restored = await _sut.TryRestoreRevisionAsync(revision, _now.AddSeconds(-1), _now.AddSeconds(1));

        Assert.That(restored, Is.False);
        Assert.That((await Context.AdventureSections.AsNoTracking().SingleAsync(s => s.Id == section.Id)).BodyMarkdown, Is.EqualTo("drafted"));
        Assert.That(await Context.AdventureSectionRevisions.CountAsync(r => r.SectionId == section.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task TryAcceptDraft_SnapshotsTheStoredBody_WritesTheNewOne_AndTrims_Together()
    {
        var adventure = await CreateAsync("the GM's text");
        var section = adventure.Sections.Single();
        await AddRevisionAsync(section.Id, "oldest", _now.AddMinutes(-3));
        await AddRevisionAsync(section.Id, "middle", _now.AddMinutes(-2));
        var at = _now.AddSeconds(1);
        var revision = new AdventureSectionRevision
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            BodyMarkdown = "ignored",
            Instruction = "Tighter.",
            Model = "nornis-ask",
            CreatedAt = at,
        };

        var accepted = await _sut.TryAcceptDraftAsync(revision, "the draft", maxRevisions: 2, basedOn: _now, updatedAt: at);

        var stored = await Context.AdventureSections.AsNoTracking().SingleAsync(s => s.Id == section.Id);
        var kept = (await Context.AdventureSectionRevisions.AsNoTracking().Where(r => r.SectionId == section.Id).ToListAsync())
            .OrderBy(r => r.CreatedAt).ToList();
        var parent = await Context.Adventures.AsNoTracking().SingleAsync(a => a.Id == adventure.Id);
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(stored.BodyMarkdown, Is.EqualTo("the draft"));
            Assert.That(stored.UpdatedAt, Is.EqualTo(at));
            Assert.That(parent.UpdatedAt, Is.EqualTo(at));
            Assert.That(kept.Select(r => r.BodyMarkdown), Is.EqualTo(["middle", "the GM's text"]),
                "the oldest is trimmed, and the new revision holds what was stored, not what was passed");
            Assert.That(kept[^1].Instruction, Is.EqualTo("Tighter."));
        });
    }

    [Test]
    public async Task TryAcceptDraft_OnAStaleStamp_WritesNothing()
    {
        var adventure = await CreateAsync("the GM's newer text");
        var section = adventure.Sections.Single();
        var revision = new AdventureSectionRevision
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            Model = "nornis-ask",
            CreatedAt = _now.AddSeconds(1),
        };

        var accepted = await _sut.TryAcceptDraftAsync(revision, "the draft", 20, basedOn: _now.AddSeconds(-1), updatedAt: _now.AddSeconds(1));

        Assert.That(accepted, Is.False);
        Assert.That((await Context.AdventureSections.AsNoTracking().SingleAsync(s => s.Id == section.Id)).BodyMarkdown,
            Is.EqualTo("the GM's newer text"));
        Assert.That(await Context.AdventureSectionRevisions.CountAsync(r => r.SectionId == section.Id), Is.Zero);
    }

    [Test]
    public async Task Delete_KeepsItsExportsAndPrints_WithTheLinkCleared()
    {
        var adventure = await CreateAsync("a");
        await AddRevisionAsync(adventure.Sections.Single().Id, "older", _now);
        var export = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Type = SourceType.AdventureExport,
            Title = "The Silver Key (prep)",
            Body = "Adventure \"The Silver Key\", exported by the GM as prep",
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _user.Id,
            CreatedAt = _now,
            AdventureId = adventure.Id,
        };
        Context.Sources.Add(export);
        await Context.SaveChangesAsync();
        await Context.LibraryDocuments.Where(d => d.Id == _print.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.AdventureId, adventure.Id));
        await Context.Adventures.Where(a => a.Id == adventure.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.LastPrintedDocumentId, _print.Id));
        Context.ChangeTracker.Clear();

        await _sut.DeleteAsync(adventure.Id);
        Context.ChangeTracker.Clear();

        var sectionId = adventure.Sections.Single().Id;
        Assert.That(await Context.Adventures.CountAsync(a => a.Id == adventure.Id), Is.Zero);
        Assert.That(await Context.AdventureSections.CountAsync(s => s.Id == sectionId), Is.Zero);
        Assert.That(await Context.AdventureSectionRevisions.CountAsync(r => r.SectionId == sectionId), Is.Zero);
        Assert.That((await Context.Sources.SingleAsync(s => s.Id == export.Id)).AdventureId, Is.Null);
        Assert.That((await Context.LibraryDocuments.SingleAsync(d => d.Id == _print.Id)).AdventureId, Is.Null);
    }

    [Test]
    public async Task Delete_ReturnsTheUploadsThatBelongedToIt_ToTheShelf()
    {
        var adventure = await CreateAsync("a");
        await Context.LibraryDocuments.Where(d => d.Id == _print.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.LinkedAdventureId, adventure.Id));
        Context.ChangeTracker.Clear();

        await _sut.DeleteAsync(adventure.Id);
        Context.ChangeTracker.Clear();

        var document = await Context.LibraryDocuments.SingleAsync(d => d.Id == _print.Id);
        Assert.That(document.LinkedAdventureId, Is.Null, "the upload outlives the Adventure, back on the Shelf");
    }

    [Test]
    public async Task DeletingTheCampaign_MakesItsPrepWorldLevel()
    {
        var adventure = await CreateAsync("a");

        await new CampaignRepository(Context).DeleteAsync(_campaign.Id);
        Context.ChangeTracker.Clear();

        var stored = await Context.Adventures.SingleAsync(a => a.Id == adventure.Id);
        Assert.That(stored.CampaignId, Is.Null);
    }

    [Test]
    public async Task DeletingThePrint_ForgetsItAsTheLastPrint()
    {
        var adventure = await CreateAsync("a");
        await Context.Adventures.Where(a => a.Id == adventure.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.LastPrintedDocumentId, _print.Id));

        await new LibraryDocumentRepository(Context).DeleteAsync(_print.Id);
        Context.ChangeTracker.Clear();

        var stored = await Context.Adventures.SingleAsync(a => a.Id == adventure.Id);
        Assert.That(stored.LastPrintedDocumentId, Is.Null);
    }

    // ------------------------------------------------- design mode (feature 37) --

    [Test]
    public async Task ADraftSave_IsConditionalOnItsStamp()
    {
        var adventure = await CreateAsync("a");
        var first = _now.AddMinutes(1);

        var started = await _sut.TrySaveDraftAsync(adventure.Id, "# Draft", null, 20, basedOn: null, first);
        var stale = await _sut.TrySaveDraftAsync(adventure.Id, "lost", null, 20, basedOn: null, _now.AddMinutes(2));
        var next = await _sut.TrySaveDraftAsync(adventure.Id, "# Draft, again", null, 20, basedOn: first, _now.AddMinutes(3));
        Context.ChangeTracker.Clear();

        var stored = await Context.Adventures.AsNoTracking().SingleAsync(a => a.Id == adventure.Id);
        Assert.Multiple(() =>
        {
            Assert.That((started, stale, next), Is.EqualTo((true, false, true)));
            Assert.That(stored.DraftMarkdown, Is.EqualTo("# Draft, again"));
        });
    }

    [Test]
    public async Task ADraftChangeWithARevision_KeepsTheOldText_TrimmedToTheBound_AndUndoRestoresIt()
    {
        var adventure = await CreateAsync("a");
        DateTimeOffset? stamp = null;
        for (var i = 0; i < 4; i++)
        {
            var at = _now.AddMinutes(i + 1);
            var revision = i == 0 ? null : new AdventureDraftRevision
            {
                Id = Guid.NewGuid(), Reason = AdventureDraftChange.Edit, CreatedAt = at,
            };
            Assert.That(await _sut.TrySaveDraftAsync(adventure.Id, $"v{i}", revision, maxRevisions: 2, stamp, at), Is.True);
            stamp = at;
        }

        Assert.That(await _sut.CountDraftRevisionsAsync(adventure.Id), Is.EqualTo(2), "v1 and v2; v0 trimmed");

        var latest = await _sut.GetLatestDraftRevisionAsync(adventure.Id);
        var restored = await _sut.TryRestoreDraftRevisionAsync(latest!, stamp, _now.AddMinutes(10));
        Context.ChangeTracker.Clear();

        var stored = await Context.Adventures.AsNoTracking().SingleAsync(a => a.Id == adventure.Id);
        var remaining = await _sut.CountDraftRevisionsAsync(adventure.Id);
        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True);
            Assert.That(stored.DraftMarkdown, Is.EqualTo("v2"));
            Assert.That(remaining, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SourceBooks_AreReadWithTheAdventure_AndLeaveWhenTheirBookIsDeleted()
    {
        var adventure = await CreateAsync("a");
        await _sut.ReplaceSourceBooksAsync(adventure.Id, [_print.Id]);

        var loaded = await _sut.GetWithSectionsAsync(adventure.Id);
        Assert.That(loaded!.SourceBooks.Select(b => b.LibraryDocument.Title), Is.EqualTo(new[] { _print.Title }));

        await new LibraryDocumentRepository(Context).DeleteAsync(_print.Id);
        Context.ChangeTracker.Clear();

        Assert.That(await Context.AdventureSourceBooks.CountAsync(b => b.AdventureId == adventure.Id), Is.Zero);
    }

    [Test]
    public async Task Delete_TakesTheDraftsHistoryAndTheSourceBookChoices_WithIt()
    {
        var adventure = await CreateAsync("a");
        await _sut.TrySaveDraftAsync(adventure.Id, "v0", null, 20, null, _now.AddMinutes(1));
        await _sut.TrySaveDraftAsync(adventure.Id, "v1",
            new AdventureDraftRevision { Id = Guid.NewGuid(), Reason = AdventureDraftChange.Put, CreatedAt = _now },
            20, _now.AddMinutes(1), _now.AddMinutes(2));
        await _sut.ReplaceSourceBooksAsync(adventure.Id, [_print.Id]);

        await _sut.DeleteAsync(adventure.Id);
        Context.ChangeTracker.Clear();

        var counts = (await Context.Adventures.CountAsync(a => a.Id == adventure.Id),
            await Context.AdventureDraftRevisions.CountAsync(r => r.AdventureId == adventure.Id),
            await Context.AdventureSourceBooks.CountAsync(b => b.AdventureId == adventure.Id),
            await Context.LibraryDocuments.CountAsync(d => d.Id == _print.Id));
        Assert.That(counts, Is.EqualTo((0, 0, 0, 1)), "the Adventure and its design rows go; the book stays");
    }

    [Test]
    public async Task DeletingTheWorld_TakesDesignModeRowsWithIt()
    {
        var adventure = await CreateAsync("a");
        await _sut.TrySaveDraftAsync(adventure.Id, "v0", null, 20, null, _now.AddMinutes(1));
        await _sut.TrySaveDraftAsync(adventure.Id, "v1",
            new AdventureDraftRevision { Id = Guid.NewGuid(), Reason = AdventureDraftChange.Gather, CreatedAt = _now },
            20, _now.AddMinutes(1), _now.AddMinutes(2));
        await _sut.ReplaceSourceBooksAsync(adventure.Id, [_print.Id]);

        await new WorldRepository(Context).DeleteAsync(_world.Id);
        Context.ChangeTracker.Clear();

        var left = await Context.AdventureSourceBooks.CountAsync(b => b.AdventureId == adventure.Id)
            + await Context.AdventureDraftRevisions.CountAsync(r => r.AdventureId == adventure.Id);
        Assert.That(left, Is.Zero);
    }
}
