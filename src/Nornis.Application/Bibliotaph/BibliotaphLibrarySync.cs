using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

/// <summary>The states the Library's Bibliotaph strip shows; the wire values of <see cref="BibliotaphShelf.State"/>.</summary>
public static class BibliotaphShelfStates
{
    /// <summary>The world's creator hasn't connected (or the integration is off): uploads only.</summary>
    public const string None = "none";

    public const string Ok = "ok";

    public const string NeedsReconnect = "needs_reconnect";

    /// <summary>The GM's library is opening; the rows shown are the last ones read.</summary>
    public const string Opening = "opening";

    /// <summary>Bibliotaph hasn't made this world's collection yet (the created event is on its way).</summary>
    public const string NotSetUp = "not_set_up";

    public const string Unreachable = "unreachable";
}

/// <summary>What the Library page says about the world's Bibliotaph, and whose it is.</summary>
public sealed record BibliotaphShelf(string State, string? OwnerName)
{
    public static readonly BibliotaphShelf None = new(BibliotaphShelfStates.None, null);
}

public interface IBibliotaphLibrarySync
{
    /// <summary>
    /// Brings the world's Library rows in line with its Bibliotaph collection, at most once per
    /// <see cref="BibliotaphOptions.ReconcileThrottleSeconds"/> per world per process. Never throws
    /// for a Bibliotaph failure: the rows stay as they were and the state says why.
    /// </summary>
    Task<BibliotaphShelf> ReconcileAsync(Guid worldId, CancellationToken ct);
}

/// <summary>
/// The per-world reconcile stamp. A singleton, because the sync is scoped and the stamp must
/// outlive a request; in memory, because one API replica runs today and a second would only
/// double the reconciles, never break one.
/// </summary>
public sealed class BibliotaphReconcileThrottle
{
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset At, BibliotaphShelf Shelf)> _last = new();

    /// <summary>
    /// True when the world is due, and stamps it (before the call, so a second tab reads the rows
    /// as they are). False hands back the last answer.
    /// </summary>
    public bool TryBegin(Guid worldId, DateTimeOffset now, TimeSpan every, string ownerName, out BibliotaphShelf last)
    {
        var placeholder = new BibliotaphShelf(BibliotaphShelfStates.Ok, ownerName);
        while (true)
        {
            if (_last.TryGetValue(worldId, out var stamp))
            {
                if (now - stamp.At < every)
                {
                    last = stamp.Shelf;
                    return false;
                }

                if (_last.TryUpdate(worldId, (now, placeholder), stamp))
                {
                    last = placeholder;
                    return true;
                }
            }
            else if (_last.TryAdd(worldId, (now, placeholder)))
            {
                last = placeholder;
                return true;
            }
        }
    }

    public void Record(Guid worldId, DateTimeOffset at, BibliotaphShelf shelf) => _last[worldId] = (at, shelf);
}

/// <summary>
/// The Library side of a world's Bibliotaph collection (feature 31, Requirement 3).
///
/// <para>
/// Matching, in order: a book Bibliotaph catalogued from a Nornis upload (<c>nornisFileId</c>)
/// marks that upload; a book whose entry id a row already holds updates it; any other book is a
/// new GMOnly row. A row whose book has left the collection is marked removed, not deleted, so
/// excerpts keep their document; a removed book that returns comes back GMOnly, since the GM
/// may have shared it under different circumstances. A title the GM changed stays theirs.
/// </para>
/// </summary>
public sealed class BibliotaphLibrarySync : IBibliotaphLibrarySync
{
    private static readonly IReadOnlyList<VisibilityScope> EveryScope = Enum.GetValues<VisibilityScope>();

    private readonly IBibliotaphConnectionResolver _owners;
    private readonly IBibliotaphClient _client;
    private readonly ILibraryDocumentRepository _documents;
    private readonly BibliotaphReconcileThrottle _throttle;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BibliotaphLibrarySync> _logger;

    public BibliotaphLibrarySync(
        IBibliotaphConnectionResolver owners,
        IBibliotaphClient client,
        ILibraryDocumentRepository documents,
        BibliotaphReconcileThrottle throttle,
        IOptions<BibliotaphOptions> options,
        TimeProvider time,
        ILogger<BibliotaphLibrarySync> logger)
    {
        _owners = owners;
        _client = client;
        _documents = documents;
        _throttle = throttle;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<BibliotaphShelf> ReconcileAsync(Guid worldId, CancellationToken ct)
    {
        var owner = await _owners.ForWorldAsync(worldId, ct);
        if (owner?.Connection is null)
        {
            return BibliotaphShelf.None;
        }

        var now = _time.GetUtcNow();
        if (!_throttle.TryBegin(worldId, now, TimeSpan.FromSeconds(_options.ReconcileThrottleSeconds), owner.DisplayName, out var last))
        {
            return last;
        }

        var books = await _client.ListBooksAsync(owner.UserId, worldId, ct);
        var shelf = books.IsSuccess
            ? await ApplyAsync(worldId, owner, books.Value!.Books, now, ct)
            : new BibliotaphShelf(StateOf(books.Failure!.Value), owner.DisplayName);

        _throttle.Record(worldId, now, shelf);
        return shelf;
    }

    private static string StateOf(BibliotaphFailure failure) => failure switch
    {
        BibliotaphFailure.NoConnection => BibliotaphShelfStates.None,
        BibliotaphFailure.NotConnected => BibliotaphShelfStates.NeedsReconnect,
        BibliotaphFailure.Opening => BibliotaphShelfStates.Opening,
        BibliotaphFailure.NotFound => BibliotaphShelfStates.NotSetUp,
        _ => BibliotaphShelfStates.Unreachable,
    };

    private async Task<BibliotaphShelf> ApplyAsync(
        Guid worldId, BibliotaphWorldOwner owner, IReadOnlyList<BibliotaphBook> books, DateTimeOffset now, CancellationToken ct)
    {
        var plan = Plan(worldId, owner.UserId, await _documents.ListByWorldAsync(worldId, EveryScope, ct), books, now);
        if (plan.Added.Count > 0 || plan.Changed.Count > 0)
        {
            try
            {
                await _documents.SaveBibliotaphReconcileAsync(plan.Added, plan.Changed, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Another process reconciled the same world at the same moment and won the unique
                // entry index. Its rows are as good as these; the next reconcile catches up.
                _logger.LogWarning(ex, "Bibliotaph reconcile of world {WorldId} lost a race; keeping the rows as they are", worldId);
            }
        }

        return new BibliotaphShelf(BibliotaphShelfStates.Ok, owner.DisplayName);
    }

    /// <summary>The rows to insert and the rows changed, from the world's rows and Bibliotaph's books. Pure.</summary>
    public static (IReadOnlyList<LibraryDocument> Added, IReadOnlyList<LibraryDocument> Changed) Plan(
        Guid worldId, Guid creatorUserId, IReadOnlyList<LibraryDocument> rows, IReadOnlyList<BibliotaphBook> books, DateTimeOffset now)
    {
        var added = new List<LibraryDocument>();
        var changed = new HashSet<LibraryDocument>();
        var byEntry = rows.Where(r => r.BibliotaphEntryId is not null).ToDictionary(r => r.BibliotaphEntryId!, StringComparer.Ordinal);
        var uploads = rows.Where(r => r.Origin == LibraryDocumentOrigin.Upload).ToDictionary(r => r.Id);
        var listed = books.Select(b => b.EntryId).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var book in books)
        {
            if (!seen.Add(book.EntryId))
            {
                continue;
            }

            if (byEntry.TryGetValue(book.EntryId, out var row))
            {
                if (Apply(row, book))
                {
                    changed.Add(row);
                }

                continue;
            }

            // An upload Bibliotaph catalogued, newly or under a new entry: it keeps its own row,
            // so it appears once and can still be downloaded.
            if (book.NornisFileId is { } fileId
                && uploads.TryGetValue(fileId, out var upload)
                && (upload.BibliotaphEntryId is null || !listed.Contains(upload.BibliotaphEntryId)))
            {
                if (upload.BibliotaphEntryId is not null)
                {
                    byEntry.Remove(upload.BibliotaphEntryId);
                }

                upload.BibliotaphEntryId = book.EntryId;
                byEntry[book.EntryId] = upload;
                Apply(upload, book);
                changed.Add(upload);
                continue;
            }

            var title = Cut(book.Title);
            var created = new LibraryDocument
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                Origin = LibraryDocumentOrigin.Bibliotaph,
                BibliotaphEntryId = book.EntryId,
                BibliotaphDocumentId = book.DocumentId,
                Title = title,
                RemoteTitle = title,
                CoverName = book.CoverName,
                Kind = LibraryDocumentKind.Sourcebook,
                Visibility = VisibilityScope.GMOnly,
                Status = LibraryDocumentStatus.Stored,
                PageCount = book.PageCount,
                UploadedByUserId = creatorUserId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            byEntry[book.EntryId] = created;
            added.Add(created);
        }

        foreach (var row in byEntry.Values.Where(r => !listed.Contains(r.BibliotaphEntryId!)).ToList())
        {
            if (row.Origin == LibraryDocumentOrigin.Bibliotaph)
            {
                if (row.RemovedAt is null)
                {
                    row.RemovedAt = now;
                    changed.Add(row);
                }
            }
            else
            {
                // An upload Bibliotaph stopped cataloguing is just an upload again.
                row.BibliotaphEntryId = null;
                row.BibliotaphDocumentId = null;
                row.CoverName = null;
                changed.Add(row);
            }
        }

        foreach (var row in changed)
        {
            row.UpdatedAt = now;
        }

        return (added, changed.ToList());

        bool Apply(LibraryDocument row, BibliotaphBook book)
        {
            var dirty = false;
            if (row.BibliotaphDocumentId != book.DocumentId)
            {
                row.BibliotaphDocumentId = book.DocumentId;
                dirty = true;
            }

            if (row.CoverName != book.CoverName)
            {
                row.CoverName = book.CoverName;
                dirty = true;
            }

            if (row.Origin != LibraryDocumentOrigin.Bibliotaph)
            {
                return dirty;
            }

            var title = Cut(book.Title);
            if (row.RemoteTitle != title)
            {
                // The GM's own title stays; an untouched one follows Bibliotaph's.
                if (row.RemoteTitle is null || row.Title == row.RemoteTitle)
                {
                    row.Title = title;
                }

                row.RemoteTitle = title;
                dirty = true;
            }

            if (row.PageCount != book.PageCount && book.PageCount is not null)
            {
                row.PageCount = book.PageCount;
                dirty = true;
            }

            if (row.RemovedAt is not null)
            {
                row.RemovedAt = null;
                row.Visibility = VisibilityScope.GMOnly;
                dirty = true;
            }

            return dirty;
        }
    }

    private static string Cut(string title)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? "Untitled book" : title.Trim();
        return trimmed.Length > LibraryService.MaxTitleLength ? trimmed[..LibraryService.MaxTitleLength] : trimmed;
    }
}
