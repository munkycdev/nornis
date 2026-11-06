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
    private readonly List<AdventureDraftRevision> _draftRevisions = [];
    private readonly List<AdventureSourceBook> _sourceBooks = [];
    private readonly List<AdventureHandout> _handouts = [];

    public IReadOnlyList<AdventureDraftRevision> DraftRevisions => _draftRevisions;

    public List<Source> Sources { get; } = [];

    public List<LibraryDocument> Documents { get; } = [];

    public IReadOnlyList<Adventure> Adventures => _adventures;

    public IReadOnlyList<AdventureSection> Sections => _sections;

    public IReadOnlyList<AdventureSectionRevision> Revisions => _revisions;

    public void SeedRevision(AdventureSectionRevision revision) => _revisions.Add(revision);

    /// <summary>Adventures a source was exported from; the fake knows no sources, so a test says which.</summary>
    public HashSet<Guid> Exported { get; } = [];

    public Task<IReadOnlyList<AdventureSummary>> ListByWorldAsync(
        Guid worldId, IReadOnlyCollection<string> unfilledBodies, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdventureSummary> list = _adventures
            .Where(a => a.WorldId == worldId)
            .Select(a =>
            {
                var sections = _sections.Where(s => s.AdventureId == a.Id).ToList();
                return new AdventureSummary(a.Id, a.Title, a.Slug, a.CampaignId, sections.Count,
                    sections.Count(s => !unfilledBodies.Contains(s.BodyMarkdown)), a.LastPrintedDocumentId, a.UpdatedAt,
                    Exported.Contains(a.Id));
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
        copy.SourceBooks = _sourceBooks.Where(b => b.AdventureId == adventureId)
            .Select(b => new AdventureSourceBook
            {
                AdventureId = b.AdventureId,
                LibraryDocumentId = b.LibraryDocumentId,
                LibraryDocument = Documents.First(d => d.Id == b.LibraryDocumentId),
            })
            .ToList();
        copy.Handouts = _handouts.Where(h => h.AdventureId == adventureId)
            .OrderBy(h => h.SortOrder)
            .Select(h => new AdventureHandout
            {
                AdventureId = h.AdventureId,
                LibraryDocumentId = h.LibraryDocumentId,
                SortOrder = h.SortOrder,
                LibraryDocument = Documents.First(d => d.Id == h.LibraryDocumentId),
            })
            .ToList();
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
        row.TableBrief = adventure.TableBrief;
        row.Details = adventure.Details with { };
        row.AiUse.Declared = adventure.AiUse.Declared;
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
        _draftRevisions.RemoveAll(r => r.AdventureId == adventureId);
        _sourceBooks.RemoveAll(b => b.AdventureId == adventureId);
        _handouts.RemoveAll(h => h.AdventureId == adventureId);
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
        DateTimeOffset basedOn, DateTimeOffset updatedAt, bool researchedOnWeb = false, CancellationToken cancellationToken = default)
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
        var adventure = Required(_adventures, a => a.Id == section.AdventureId);
        adventure.AiUse.WroteText = true;
        adventure.AiUse.ResearchedOnWeb |= researchedOnWeb;
        return Task.FromResult(true);
    }

    public Task SetCoverAsync(
        Guid adventureId, AdventureCover? cover, bool generated, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventureId);
        row.Cover = cover is null ? null : cover with { };
        row.UpdatedAt = updatedAt;
        if (generated)
        {
            row.AiUse.MadeArt = true;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryCountCoverImagesAsync(Guid adventureId, int count, int cap, CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventureId);
        if (row.CoverImagesGenerated + count > cap)
        {
            return Task.FromResult(false);
        }

        row.CoverImagesGenerated += count;
        return Task.FromResult(true);
    }

    public Task ReleaseCoverImagesAsync(Guid adventureId, int count, CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventureId);
        row.CoverImagesGenerated = Math.Max(0, row.CoverImagesGenerated - count);
        return Task.CompletedTask;
    }

    public Task SaveCoverWorkAsync(
        Guid adventureId, string? brief, IReadOnlyList<Guid> newOptionIds, CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventureId);
        row.CoverBrief = brief;
        row.CoverOptionIds = [.. row.CoverOptionIds, .. newOptionIds.Where(id => !row.CoverOptionIds.Contains(id))];
        return Task.CompletedTask;
    }

    public Task<bool> TrySaveDraftAsync(
        Guid adventureId, string? markdown, AdventureDraftRevision? revision, int maxRevisions,
        DateTimeOffset? basedOn, DateTimeOffset updatedAt, bool loremasterWrote = false, bool researchedOnWeb = false,
        CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventureId);
        if (row.DraftUpdatedAt != basedOn)
        {
            return Task.FromResult(false);
        }

        if (revision is not null && row.DraftMarkdown is { } before)
        {
            var stale = _draftRevisions.Where(r => r.AdventureId == adventureId)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Skip(Math.Max(maxRevisions - 1, 0)).ToList();
            _draftRevisions.RemoveAll(stale.Contains);
            _draftRevisions.Add(new AdventureDraftRevision
            {
                Id = revision.Id,
                AdventureId = adventureId,
                BodyMarkdown = before,
                Reason = revision.Reason,
                CreatedAt = revision.CreatedAt,
            });
        }

        row.DraftMarkdown = markdown;
        row.DraftUpdatedAt = markdown is null ? null : updatedAt;
        row.UpdatedAt = updatedAt;
        if (loremasterWrote)
        {
            row.AiUse.WroteText = true;
            row.AiUse.ResearchedOnWeb |= researchedOnWeb;
        }

        return Task.FromResult(true);
    }

    public Task<bool> TryWriteSplitAsync(
        Guid adventureId, IReadOnlyList<AdventureSection> sections, bool replace, AdventureDraftRevision? revision,
        int maxRevisions, DateTimeOffset? basedOn, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == adventureId);
        if (row.DraftUpdatedAt != basedOn)
        {
            return Task.FromResult(false);
        }

        var next = 1;
        if (replace)
        {
            var ids = _sections.Where(x => x.AdventureId == adventureId).Select(x => x.Id).ToHashSet();
            _revisions.RemoveAll(r => ids.Contains(r.SectionId));
            _sections.RemoveAll(x => ids.Contains(x.Id));
        }
        else
        {
            next = _sections.Where(x => x.AdventureId == adventureId).Select(x => x.SortOrder).DefaultIfEmpty(0).Max() + 1;
        }

        foreach (var section in sections)
        {
            var copy = CopyOf(section);
            copy.AdventureId = adventureId;
            copy.SortOrder = next++;
            copy.UpdatedAt = updatedAt;
            _sections.Add(copy);
        }

        if (revision is not null)
        {
            var stale = _draftRevisions.Where(r => r.AdventureId == adventureId)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Skip(Math.Max(maxRevisions - 1, 0)).ToList();
            _draftRevisions.RemoveAll(stale.Contains);
            _draftRevisions.Add(new AdventureDraftRevision
            {
                Id = revision.Id,
                AdventureId = adventureId,
                BodyMarkdown = revision.BodyMarkdown,
                Reason = revision.Reason,
                CreatedAt = revision.CreatedAt,
            });
        }

        row.UpdatedAt = updatedAt;
        return Task.FromResult(true);
    }

    public Task<int> CountDraftRevisionsAsync(Guid adventureId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_draftRevisions.Count(r => r.AdventureId == adventureId));

    public Task<AdventureDraftRevision?> GetLatestDraftRevisionAsync(Guid adventureId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_draftRevisions.Where(r => r.AdventureId == adventureId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefault());

    public Task<bool> TryRestoreDraftRevisionAsync(
        AdventureDraftRevision revision, DateTimeOffset? basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var row = Required(_adventures, a => a.Id == revision.AdventureId);
        if (row.DraftUpdatedAt != basedOn)
        {
            return Task.FromResult(false);
        }

        row.DraftMarkdown = revision.BodyMarkdown;
        row.DraftUpdatedAt = updatedAt;
        row.UpdatedAt = updatedAt;
        _draftRevisions.RemoveAll(r => r.Id == revision.Id);
        return Task.FromResult(true);
    }

    public Task ReplaceHandoutsAsync(Guid adventureId, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        _handouts.RemoveAll(h => h.AdventureId == adventureId);
        _handouts.AddRange(documentIds.Select((id, i) => new AdventureHandout { AdventureId = adventureId, LibraryDocumentId = id, SortOrder = i + 1 }));
        return Task.CompletedTask;
    }

    public Task ReplaceSourceBooksAsync(Guid adventureId, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        _sourceBooks.RemoveAll(b => b.AdventureId == adventureId);
        _sourceBooks.AddRange(documentIds.Select(id => new AdventureSourceBook { AdventureId = adventureId, LibraryDocumentId = id }));
        return Task.CompletedTask;
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
        Details = a.Details with { },
        AiUse = new() { WroteText = a.AiUse.WroteText, MadeArt = a.AiUse.MadeArt, Declared = a.AiUse.Declared },
        Cover = a.Cover is null ? null : a.Cover with { },
        CoverImagesGenerated = a.CoverImagesGenerated,
        CoverBrief = a.CoverBrief,
        CoverOptionIds = [.. a.CoverOptionIds],
        DraftMarkdown = a.DraftMarkdown,
        DraftUpdatedAt = a.DraftUpdatedAt,
        TableBrief = a.TableBrief,
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
