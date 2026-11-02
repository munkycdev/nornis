using Nornis.Domain.Entities;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Prep's repository in memory, with the real one's contract: reads hand back copies (the
/// real reads are untracked), section writes are conditional on the stamp, and a delete
/// detaches the exports and prints it is given. Sources and documents are plain lists here,
/// enough to see the detach.
/// </summary>
public class InMemoryAdventureRepository : IAdventureRepository
{
    private readonly List<Adventure> _adventures = [];
    private readonly List<AdventureSection> _sections = [];
    private readonly List<AdventureSectionRevision> _revisions = [];

    public List<Source> Sources { get; } = [];

    public List<LibraryDocument> Documents { get; } = [];

    public IReadOnlyList<Adventure> Adventures => _adventures;

    public IReadOnlyList<AdventureSection> Sections => _sections;

    public IReadOnlyList<AdventureSectionRevision> Revisions => _revisions;

    public void SeedRevision(AdventureSectionRevision revision) => _revisions.Add(revision);

    public Task<IReadOnlyList<AdventureSummary>> ListByWorldAsync(
        Guid worldId, IReadOnlyCollection<string> unfilledBodies, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdventureSummary> list = _adventures
            .Where(a => a.WorldId == worldId)
            .Select(a =>
            {
                var sections = _sections.Where(s => s.AdventureId == a.Id).ToList();
                return new AdventureSummary(a.Id, a.Title, a.Slug, a.CampaignId, sections.Count,
                    sections.Count(s => !unfilledBodies.Contains(s.BodyMarkdown)), a.LastPrintedDocumentId, a.UpdatedAt);
            })
            .OrderByDescending(a => a.UpdatedAt)
            .ToList();
        return Task.FromResult(list);
    }

    public Task<Adventure?> GetWithSectionsAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var stored = _adventures.FirstOrDefault(a => a.Id == adventureId);
        if (stored is null)
        {
            return Task.FromResult<Adventure?>(null);
        }

        var copy = CopyOf(stored);
        copy.Sections = _sections.Where(s => s.AdventureId == adventureId).OrderBy(s => s.SortOrder).Select(CopyOf).ToList();
        return Task.FromResult<Adventure?>(copy);
    }

    public Task<Adventure> CreateAsync(Adventure adventure, CancellationToken cancellationToken = default)
    {
        adventure.Slug ??= adventure.Title.ToLowerInvariant().Replace(' ', '-');
        _adventures.Add(CopyOf(adventure));
        _sections.AddRange(adventure.Sections.Select(CopyOf));
        return Task.FromResult(adventure);
    }

    public Task UpdateAsync(Adventure adventure, CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventure.Id);
        row.Title = adventure.Title;
        row.Pitch = adventure.Pitch;
        row.CampaignId = adventure.CampaignId;
        row.LastPrintedDocumentId = adventure.LastPrintedDocumentId;
        row.UpdatedAt = adventure.UpdatedAt;
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateSectionAsync(
        AdventureSection section, DateTimeOffset basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var row = Required(_sections, s => s.Id == section.Id);
        if (row.UpdatedAt != basedOn)
        {
            return Task.FromResult(false);
        }

        row.Kind = section.Kind;
        row.Title = section.Title;
        row.BodyMarkdown = section.BodyMarkdown;
        row.UpdatedAt = updatedAt;
        return Task.FromResult(true);
    }

    public Task InsertSectionAsync(AdventureSection section, Guid? afterSectionId, CancellationToken cancellationToken = default)
    {
        var ordered = _sections.Where(s => s.AdventureId == section.AdventureId).OrderBy(s => s.SortOrder).ToList();
        var copy = CopyOf(section);
        ordered.Insert(afterSectionId is { } after ? ordered.FindIndex(s => s.Id == after) + 1 : 0, copy);
        Renumber(ordered);
        _sections.Add(copy);
        return Task.CompletedTask;
    }

    public Task DeleteSectionAsync(Guid adventureId, Guid sectionId, CancellationToken cancellationToken = default)
    {
        _revisions.RemoveAll(r => r.SectionId == sectionId);
        _sections.RemoveAll(s => s.Id == sectionId);
        Renumber(_sections.Where(s => s.AdventureId == adventureId).OrderBy(s => s.SortOrder).ToList());
        return Task.CompletedTask;
    }

    public Task ReorderSectionsAsync(Guid adventureId, IReadOnlyList<Guid> orderedSectionIds, CancellationToken cancellationToken = default)
    {
        Renumber(orderedSectionIds.Select(id => Required(_sections, s => s.Id == id)).ToList());
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        foreach (var source in Sources.Where(s => s.AdventureId == adventureId))
        {
            source.AdventureId = null;
        }

        foreach (var document in Documents.Where(d => d.AdventureId == adventureId))
        {
            document.AdventureId = null;
        }

        var sectionIds = _sections.Where(s => s.AdventureId == adventureId).Select(s => s.Id).ToHashSet();
        _revisions.RemoveAll(r => sectionIds.Contains(r.SectionId));
        _sections.RemoveAll(s => s.AdventureId == adventureId);
        _adventures.RemoveAll(a => a.Id == adventureId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, int>> CountRevisionsAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var sectionIds = _sections.Where(s => s.AdventureId == adventureId).Select(s => s.Id).ToHashSet();
        IReadOnlyDictionary<Guid, int> counts = _revisions
            .Where(r => sectionIds.Contains(r.SectionId))
            .GroupBy(r => r.SectionId)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(counts);
    }

    public Task<AdventureSectionRevision?> GetLatestRevisionAsync(Guid sectionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.Where(r => r.SectionId == sectionId).MaxBy(r => r.CreatedAt));

    public Task<bool> TryRestoreRevisionAsync(
        AdventureSectionRevision revision, DateTimeOffset basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var section = Required(_sections, s => s.Id == revision.SectionId);
        if (section.UpdatedAt != basedOn)
        {
            return Task.FromResult(false);
        }

        section.BodyMarkdown = revision.BodyMarkdown;
        section.UpdatedAt = updatedAt;
        _revisions.RemoveAll(r => r.Id == revision.Id);
        return Task.FromResult(true);
    }

    public Task<bool> TryAcceptDraftAsync(
        AdventureSectionRevision revision, string newBody, int maxRevisions,
        DateTimeOffset basedOn, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var section = Required(_sections, s => s.Id == revision.SectionId);
        if (section.UpdatedAt != basedOn)
        {
            return Task.FromResult(false);
        }

        var stale = _revisions.Where(r => r.SectionId == section.Id)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip(Math.Max(maxRevisions - 1, 0)).Select(r => r.Id).ToHashSet();
        _revisions.RemoveAll(r => stale.Contains(r.Id));
        _revisions.Add(new AdventureSectionRevision
        {
            Id = revision.Id,
            SectionId = revision.SectionId,
            BodyMarkdown = section.BodyMarkdown,
            Instruction = revision.Instruction,
            Model = revision.Model,
            CreatedAt = revision.CreatedAt,
        });
        section.BodyMarkdown = newBody;
        section.UpdatedAt = updatedAt;
        return Task.FromResult(true);
    }

    private static void Renumber(List<AdventureSection> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i + 1;
        }
    }

    private static T Required<T>(List<T> rows, Func<T, bool> match) =>
        rows.FirstOrDefault(match) ?? throw new InvalidOperationException($"{typeof(T).Name} not found.");

    private static Adventure CopyOf(Adventure a) => new()
    {
        Id = a.Id,
        WorldId = a.WorldId,
        CampaignId = a.CampaignId,
        Title = a.Title,
        Slug = a.Slug,
        Pitch = a.Pitch,
        LastPrintedDocumentId = a.LastPrintedDocumentId,
        CreatedByUserId = a.CreatedByUserId,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };

    private static AdventureSection CopyOf(AdventureSection s) => new()
    {
        Id = s.Id,
        AdventureId = s.AdventureId,
        Kind = s.Kind,
        Title = s.Title,
        BodyMarkdown = s.BodyMarkdown,
        SortOrder = s.SortOrder,
        UpdatedAt = s.UpdatedAt,
    };
}
