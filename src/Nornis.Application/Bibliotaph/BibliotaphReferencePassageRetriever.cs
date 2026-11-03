using Microsoft.Extensions.Logging;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

/// <summary>
/// The world's reference books from its Bibliotaph collection (feature 31, Requirement 4),
/// alongside Nornis's own indexed uploads (the inner retriever) until the cutover removes them.
///
/// <para>
/// What a question may search is decided here and nowhere else. A caller allowed GMOnly
/// searches the whole collection (no <c>entryIds</c>). Any other caller sends the entry ids of
/// the world's live PartyVisible rows and makes no call at all when there are none, so "absent"
/// never means "players, everything". Pages come back mapped onto the rows the caller may see,
/// so a page from any other book is dropped even if Bibliotaph sent it.
/// </para>
///
/// <para>
/// A failure gives no Bibliotaph passages and says the reference books were unavailable; the
/// question is still answered from world memory and the uploads.
/// </para>
/// </summary>
public sealed class BibliotaphReferencePassageRetriever : IReferencePassageRetriever
{
    /// <summary>Pages asked for per question; the contract's own example limit.</summary>
    public const int PassageLimit = 10;

    private readonly IReferencePassageRetriever _uploads;
    private readonly IBibliotaphConnectionResolver _owners;
    private readonly IBibliotaphClient _client;
    private readonly ILibraryDocumentRepository _documents;
    private readonly IBibliotaphLibrarySync _sync;
    private readonly ILogger<BibliotaphReferencePassageRetriever> _logger;

    public BibliotaphReferencePassageRetriever(
        IReferencePassageRetriever uploads,
        IBibliotaphConnectionResolver owners,
        IBibliotaphClient client,
        ILibraryDocumentRepository documents,
        IBibliotaphLibrarySync sync,
        ILogger<BibliotaphReferencePassageRetriever> logger)
    {
        _uploads = uploads;
        _owners = owners;
        _client = client;
        _documents = documents;
        _sync = sync;
        _logger = logger;
    }

    public async Task<ReferencePassages> RetrieveAsync(
        string question, Guid worldId, Guid userId, WorldRole role, CancellationToken ct) =>
        await CombineAsync(
            question, worldId, LibraryService.GetAllowedScopes(role), ReferenceSearchMode.Ask,
            await _uploads.RetrieveAsync(question, worldId, userId, role, ct), ct);

    public async Task<ReferencePassages> RetrieveForScopesAsync(
        string query, Guid worldId, IReadOnlyList<VisibilityScope> allowedScopes, Guid? attributedUserId,
        ReferenceSearchMode mode, CancellationToken ct) =>
        await CombineAsync(
            query, worldId, allowedScopes, mode,
            await _uploads.RetrieveForScopesAsync(query, worldId, allowedScopes, attributedUserId, mode, ct), ct);

    /// <summary>
    /// An Adventure's source books (feature 37): Bibliotaph is asked for the chosen books' entries
    /// only, and the uploads among them come from the inner retriever. GM reach, because Prep is
    /// GM-only; a book that left the collection simply contributes nothing.
    /// </summary>
    public async Task<ReferencePassages> RetrieveFromDocumentsAsync(
        string query, Guid worldId, Guid userId, IReadOnlyList<Guid> documentIds, CancellationToken ct)
    {
        var uploads = await _uploads.RetrieveFromDocumentsAsync(query, worldId, userId, documentIds, ct);
        var owner = await _owners.ForWorldAsync(worldId, ct);
        if (owner?.Connection is null)
        {
            return uploads;
        }

        var rows = (await LiveRowsAsync(worldId, LibraryService.GetAllowedScopes(WorldRole.GM), ct))
            .Where(r => documentIds.Contains(r.Value.Id))
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        if (rows.Count == 0)
        {
            return uploads;
        }

        var result = await _client.FindPassagesAsync(
            owner.UserId, worldId, new BibliotaphPassagesRequest(query, PassageLimit, rows.Keys.ToList(), BibliotaphPassageModes.Ask), ct);
        if (!result.IsSuccess)
        {
            _logger.LogInformation(
                "Bibliotaph passages for world {WorldId} unavailable ({Failure})", worldId, result.Failure);
            return uploads with { Unavailable = true };
        }

        var fromBibliotaph = result.Value!.Pages
            .Where(p => rows.ContainsKey(p.EntryId))
            .Select(p => ToPassage(rows[p.EntryId], p))
            .ToList();
        var covered = rows.Values.Select(r => r.Id).ToHashSet();
        return new ReferencePassages(
            fromBibliotaph.Concat(uploads.Passages.Where(p => !covered.Contains(p.DocumentId))).ToList(), uploads.Unavailable);
    }

    private async Task<ReferencePassages> CombineAsync(
        string question, Guid worldId, IReadOnlyList<VisibilityScope> scopes, ReferenceSearchMode mode,
        ReferencePassages uploads, CancellationToken ct)
    {
        var owner = await _owners.ForWorldAsync(worldId, ct);
        if (owner?.Connection is null || scopes.Count == 0)
        {
            return uploads;
        }

        var wholeCollection = scopes.Contains(VisibilityScope.GMOnly);
        var rows = await LiveRowsAsync(worldId, scopes, ct);
        IReadOnlyList<string>? entryIds = null;
        if (!wholeCollection)
        {
            entryIds = rows.Values.Where(r => r.Visibility == VisibilityScope.PartyVisible).Select(r => r.BibliotaphEntryId!).ToList();
            if (entryIds.Count == 0)
            {
                return uploads;
            }
        }

        var result = await _client.FindPassagesAsync(
            owner.UserId, worldId, new BibliotaphPassagesRequest(question, PassageLimit, entryIds,
                mode == ReferenceSearchMode.Search ? BibliotaphPassageModes.Search : BibliotaphPassageModes.Ask), ct);
        if (!result.IsSuccess)
        {
            _logger.LogInformation(
                "Bibliotaph passages for world {WorldId} unavailable ({Failure})", worldId, result.Failure);
            return uploads with { Unavailable = true };
        }

        var pages = result.Value!.Pages;

        // A GM's book newer than the rows: one reconcile, inside the throttle, then map again.
        if (wholeCollection && pages.Any(p => !rows.ContainsKey(p.EntryId)))
        {
            await _sync.ReconcileAsync(worldId, ct);
            rows = await LiveRowsAsync(worldId, scopes, ct);
        }

        var fromBibliotaph = pages
            .Where(p => rows.ContainsKey(p.EntryId))
            .Select(p => ToPassage(rows[p.EntryId], p))
            .ToList();

        // An upload Bibliotaph catalogued would otherwise be quoted twice: Bibliotaph's pages win.
        var covered = rows.Values.Select(r => r.Id).ToHashSet();
        var merged = fromBibliotaph
            .Concat(uploads.Passages.Where(p => !covered.Contains(p.DocumentId)))
            .ToList();
        return new ReferencePassages(merged, uploads.Unavailable);
    }

    private static KnowledgePassage ToPassage(LibraryDocument row, BibliotaphPassage page) => new()
    {
        ChunkId = null,
        DocumentId = row.Id,
        DocumentTitle = row.Title,
        DocumentSlug = row.Slug,
        Page = BibliotaphPages.ToPageNumber(page.PageIndex),
        PageLabel = page.PageLabel,
        Text = page.Text,
        ReferenceId = $"passage:{row.Id:N}-{page.PageIndex}",
    };

    /// <summary>The world's rows in Bibliotaph's collection that these scopes may see, by entry id.</summary>
    private async Task<Dictionary<string, LibraryDocument>> LiveRowsAsync(
        Guid worldId, IReadOnlyList<VisibilityScope> scopes, CancellationToken ct)
    {
        var rows = await _documents.ListByWorldAsync(worldId, scopes, ct);
        return rows
            .Where(r => r.BibliotaphEntryId is not null && r.RemovedAt is null)
            .GroupBy(r => r.BibliotaphEntryId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }
}
