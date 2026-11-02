using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAdventureRepository"/>
public class AdventureRepository : IAdventureRepository
{
    private readonly NornisDbContext _context;

    public AdventureRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<AdventureSummary>> ListByWorldAsync(
        Guid worldId, IReadOnlyCollection<string> unfilledBodies, CancellationToken cancellationToken = default)
    {
        var rows = await _context.Adventures
            .AsNoTracking()
            .Where(a => a.WorldId == worldId)
            .Select(a => new AdventureSummary(
                a.Id,
                a.Title,
                a.Slug,
                a.CampaignId,
                a.Sections.Count,
                a.Sections.Count(s => !unfilledBodies.Contains(s.BodyMarkdown)),
                a.LastPrintedDocumentId,
                a.UpdatedAt))
            .ToListAsync(cancellationToken);

        // Newest work first. Ordered here rather than in SQL so the SQLite test provider, which
        // can't order a DateTimeOffset, runs the same query production does.
        return rows.OrderByDescending(a => a.UpdatedAt).ToList();
    }

    public async Task<Adventure?> GetWithSectionsAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var adventure = await _context.Adventures
            .AsNoTracking()
            .Include(a => a.Sections)
            .FirstOrDefaultAsync(a => a.Id == adventureId, cancellationToken);

        if (adventure is not null)
        {
            adventure.Sections = adventure.Sections.OrderBy(s => s.SortOrder).ToList();
        }

        return adventure;
    }

    public async Task<Adventure> CreateAsync(Adventure adventure, CancellationToken cancellationToken = default)
    {
        // The sections travel on the navigation and are inserted with the Adventure, in one save.
        _context.Adventures.Add(adventure);
        await _context.SaveChangesAsync(cancellationToken);
        Detach(adventure);
        return adventure;
    }

    public async Task UpdateAsync(Adventure adventure, CancellationToken cancellationToken = default)
    {
        var row = await _context.LoadForUpdateAsync<Adventure>(adventure.Id, cancellationToken);
        row.Title = adventure.Title;
        row.Pitch = adventure.Pitch;
        row.CampaignId = adventure.CampaignId;
        row.LastPrintedDocumentId = adventure.LastPrintedDocumentId;
        row.UpdatedAt = adventure.UpdatedAt;
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(row).State = EntityState.Detached;
    }

    public async Task<bool> TryUpdateSectionAsync(
        AdventureSection section, DateTimeOffset basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var row = await _context.LoadForUpdateAsync<AdventureSection>(section.Id, cancellationToken);
        if (row.UpdatedAt != basedOn)
        {
            _context.Entry(row).State = EntityState.Detached;
            return false;
        }

        row.Kind = section.Kind;
        row.Title = section.Title;
        row.BodyMarkdown = section.BodyMarkdown;
        row.UpdatedAt = updatedAt;
        await TouchAdventureAsync(row.AdventureId, updatedAt, cancellationToken);

        return await SaveGuardedAsync(cancellationToken);
    }

    public async Task InsertSectionAsync(
        AdventureSection section, Guid? afterSectionId, CancellationToken cancellationToken = default)
    {
        var siblings = await _context.AdventureSections
            .Where(s => s.AdventureId == section.AdventureId)
            .ToListAsync(cancellationToken);

        var ordered = siblings.OrderBy(s => s.SortOrder).ToList();
        var at = afterSectionId is { } after ? ordered.FindIndex(s => s.Id == after) + 1 : 0;
        ordered.Insert(at, section);
        Renumber(ordered);

        _context.AdventureSections.Add(section);
        await TouchAdventureAsync(section.AdventureId, section.UpdatedAt, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task DeleteSectionAsync(Guid adventureId, Guid sectionId, CancellationToken cancellationToken = default)
    {
        await _context.DeleteWhereAsync<AdventureSectionRevision>(r => r.SectionId == sectionId, cancellationToken);
        await _context.DeleteWhereAsync<AdventureSection>(s => s.Id == sectionId, cancellationToken);

        var remaining = await _context.AdventureSections
            .Where(s => s.AdventureId == adventureId)
            .ToListAsync(cancellationToken);
        Renumber(remaining.OrderBy(s => s.SortOrder).ToList());
        await TouchAdventureAsync(adventureId, DateTimeOffset.UtcNow, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task ReorderSectionsAsync(
        Guid adventureId, IReadOnlyList<Guid> orderedSectionIds, CancellationToken cancellationToken = default)
    {
        var sections = await _context.AdventureSections
            .Where(s => s.AdventureId == adventureId)
            .ToListAsync(cancellationToken);

        var byId = sections.ToDictionary(s => s.Id);
        Renumber(orderedSectionIds.Select(id => byId[id]).ToList());
        await TouchAdventureAsync(adventureId, DateTimeOffset.UtcNow, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        // Its prints are exposed to Bibliotaph because they belong to it; letting go below takes
        // them out of the listing (feature 31). One rescan, even for a print that stays exposed.
        var printWorldId = await _context.LibraryDocuments.AsNoTracking()
            .Where(d => d.AdventureId == adventureId)
            .Where(BibliotaphExposure.Rule)
            .Select(d => (Guid?)d.WorldId)
            .FirstOrDefaultAsync(cancellationToken);
        if (printWorldId is { } worldId
            && await BibliotaphOutboxCapture.QueueAsync(
                _context, worldId, BibliotaphWorldEventKind.FilesChanged, DateTimeOffset.UtcNow, cancellationToken))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Exports and prints are snapshots and outlive the Adventure. Both FKs are Restrict
        // (Worlds cascades to every table here), so they are let go of before the delete.
        await _context.SetWhereAsync<Source, Guid?>(
            s => s.AdventureId == adventureId, s => s.AdventureId, null, cancellationToken);
        await _context.SetWhereAsync<LibraryDocument, Guid?>(
            d => d.AdventureId == adventureId, d => d.AdventureId, null, cancellationToken);

        // The side conversation is the Adventure's own, and goes with it (its FK is Restrict).
        await _context.DeleteWhereAsync<ConversationMessage>(
            m => _context.Conversations.Any(c => c.Id == m.ConversationId && c.AdventureId == adventureId),
            cancellationToken);
        await _context.DeleteWhereAsync<Conversation>(c => c.AdventureId == adventureId, cancellationToken);

        await _context.DeleteWhereAsync<AdventureSectionRevision>(
            r => _context.AdventureSections.Any(s => s.Id == r.SectionId && s.AdventureId == adventureId),
            cancellationToken);
        await _context.DeleteWhereAsync<AdventureSection>(s => s.AdventureId == adventureId, cancellationToken);
        await _context.DeleteWhereAsync<Adventure>(a => a.Id == adventureId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountRevisionsAsync(
        Guid adventureId, CancellationToken cancellationToken = default)
    {
        return await _context.AdventureSectionRevisions
            .AsNoTracking()
            .Where(r => _context.AdventureSections.Any(s => s.Id == r.SectionId && s.AdventureId == adventureId))
            .GroupBy(r => r.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SectionId, g => g.Count, cancellationToken);
    }

    public async Task<AdventureSectionRevision?> GetLatestRevisionAsync(
        Guid sectionId, CancellationToken cancellationToken = default)
    {
        // Bounded by PrepOptions.MaxRevisionsPerSection, so reading them all to pick the newest
        // costs at most that many rows, and runs the same on SQLite as on SQL Server.
        var revisions = await _context.AdventureSectionRevisions
            .AsNoTracking()
            .Where(r => r.SectionId == sectionId)
            .ToListAsync(cancellationToken);

        return revisions.MaxBy(r => r.CreatedAt);
    }

    public async Task<bool> TryRestoreRevisionAsync(
        AdventureSectionRevision revision, DateTimeOffset basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var section = await _context.LoadForUpdateAsync<AdventureSection>(revision.SectionId, cancellationToken);
        if (section.UpdatedAt != basedOn)
        {
            _context.Entry(section).State = EntityState.Detached;
            return false;
        }

        section.BodyMarkdown = revision.BodyMarkdown;
        section.UpdatedAt = updatedAt;
        var row = await _context.LoadForUpdateAsync<AdventureSectionRevision>(revision.Id, cancellationToken);
        _context.AdventureSectionRevisions.Remove(row);
        await TouchAdventureAsync(section.AdventureId, updatedAt, cancellationToken);

        // One SaveChanges: the body and the revision's removal commit together or not at all.
        return await SaveGuardedAsync(cancellationToken);
    }

    public async Task<bool> TryAcceptDraftAsync(
        AdventureSectionRevision revision, string newBody, int maxRevisions,
        DateTimeOffset basedOn, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var section = await _context.LoadForUpdateAsync<AdventureSection>(revision.SectionId, cancellationToken);
        if (section.UpdatedAt != basedOn)
        {
            _context.Entry(section).State = EntityState.Detached;
            return false;
        }

        // Keep the newest maxRevisions - 1 of the old ones, so with this one there are maxRevisions.
        // Ordered in memory, as GetLatestRevisionAsync is: the set is bounded, and SQLite can't
        // order by a DateTimeOffset.
        var existing = await _context.AdventureSectionRevisions
            .Where(r => r.SectionId == section.Id)
            .ToListAsync(cancellationToken);
        var stale = existing
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(Math.Max(maxRevisions - 1, 0));
        _context.AdventureSectionRevisions.RemoveRange(stale);

        revision.BodyMarkdown = section.BodyMarkdown;
        _context.AdventureSectionRevisions.Add(revision);

        section.BodyMarkdown = newBody;
        section.UpdatedAt = updatedAt;
        await TouchAdventureAsync(section.AdventureId, updatedAt, cancellationToken);

        // One SaveChanges: the revision, the trim and the new body commit together or not at all.
        return await SaveGuardedAsync(cancellationToken);
    }

    private static void Renumber(IReadOnlyList<AdventureSection> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i + 1;
        }
    }

    private async Task TouchAdventureAsync(Guid adventureId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var adventure = await _context.LoadForUpdateAsync<Adventure>(adventureId, cancellationToken);
        adventure.UpdatedAt = at;
    }

    /// <summary>
    /// Saves, reporting a lost race on a section's stamp as false. The stamp is checked before
    /// the save too, so this only fires when another writer got in between the read and the
    /// UPDATE, whose WHERE carries the stamp the row was read with.
    /// </summary>
    private async Task<bool> SaveGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private void Detach(Adventure adventure)
    {
        foreach (var section in adventure.Sections)
        {
            _context.Entry(section).State = EntityState.Detached;
        }

        _context.Entry(adventure).State = EntityState.Detached;
    }
}
