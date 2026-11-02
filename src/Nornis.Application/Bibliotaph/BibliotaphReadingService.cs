using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

/// <summary>Where opening a book sends the browser. <see cref="Kind"/> is <c>reader</c> or <c>download</c>.</summary>
public sealed record LibraryReadLink(string Url, string Kind)
{
    public const string Reader = "reader";
    public const string Download = "download";
}

/// <summary>One open of a Bibliotaph book, for the GM's "Opened by".</summary>
public sealed record LibraryOpen(string ViewerName, DateTimeOffset OpenedAt, bool FromShelfLink);

public interface IBibliotaphReadingService
{
    /// <summary>A member opens a book they may see. <paramref name="startPage"/> is 0-based.</summary>
    Task<AppResult<LibraryReadLink>> OpenForMemberAsync(
        Guid worldId, Guid documentId, User member, WorldRole role, string viewerName, int? startPage, CancellationToken ct);

    /// <summary>A shelf link's player opens a shared book. <paramref name="startPage"/> is 0-based.</summary>
    Task<AppResult<LibraryReadLink>> OpenForShelfAsync(string code, Guid documentId, int? startPage, CancellationToken ct);

    /// <summary>GM only: the book's latest opens, newest first.</summary>
    Task<AppResult<IReadOnlyList<LibraryOpen>>> ListOpensAsync(Guid worldId, Guid documentId, WorldRole role, CancellationToken ct);
}

/// <summary>
/// Players read shared books through a reading ticket (feature 31, Requirement 4). Nornis decides
/// who may read (the Library's own visibility check, or the shelf's), Bibliotaph checks the book
/// is in the world's collection, and every open is recorded. The ticket carries a renew address
/// on Nornis, the read page itself, so an hour later the same checks run again.
/// </summary>
public sealed class BibliotaphReadingService : IBibliotaphReadingService
{
    public const int OpensShown = 50;

    private readonly ILibraryService _library;
    private readonly IShelfLinkService _shelf;
    private readonly IBibliotaphConnectionResolver _owners;
    private readonly IBibliotaphClient _client;
    private readonly ILibraryReadingRecordRepository _records;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BibliotaphReadingService> _logger;

    public BibliotaphReadingService(
        ILibraryService library,
        IShelfLinkService shelf,
        IBibliotaphConnectionResolver owners,
        IBibliotaphClient client,
        ILibraryReadingRecordRepository records,
        IOptions<BibliotaphOptions> options,
        TimeProvider time,
        ILogger<BibliotaphReadingService> logger)
    {
        _library = library;
        _shelf = shelf;
        _owners = owners;
        _client = client;
        _records = records;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<AppResult<LibraryReadLink>> OpenForMemberAsync(
        Guid worldId, Guid documentId, User member, WorldRole role, string viewerName, int? startPage, CancellationToken ct)
    {
        var found = await _library.GetByIdAsync(documentId, worldId, role, ct);
        if (!found.IsSuccess)
        {
            return AppResult<LibraryReadLink>.Fail(found.Error!);
        }

        var document = found.Value!;
        if (document.BibliotaphDocumentId is null)
        {
            return await DownloadAsync(_library.GetDownloadAsync(documentId, worldId, role, ct));
        }

        var renew = $"/library/{document.Slug ?? document.Id.ToString()}/read";
        return await MintAsync(worldId, document, renew, viewerName, startPage,
            new LibraryReadingRecord { UserId = member.Id }, ct);
    }

    public async Task<AppResult<LibraryReadLink>> OpenForShelfAsync(string code, Guid documentId, int? startPage, CancellationToken ct)
    {
        var found = await _shelf.GetDocumentAsync(code, documentId, ct);
        if (!found.IsSuccess)
        {
            return AppResult<LibraryReadLink>.Fail(found.Error!);
        }

        var shelf = found.Value!;
        if (shelf.Document.BibliotaphDocumentId is null)
        {
            return await DownloadAsync(_shelf.DownloadAsync(code, documentId, ct));
        }

        var renew = $"/shelf/{Uri.EscapeDataString(code)}/read/{documentId}";
        return await MintAsync(shelf.WorldId, shelf.Document, renew, shelf.PlayerName, startPage,
            new LibraryReadingRecord { PlayerId = shelf.PlayerId }, ct);
    }

    public async Task<AppResult<IReadOnlyList<LibraryOpen>>> ListOpensAsync(
        Guid worldId, Guid documentId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<IReadOnlyList<LibraryOpen>>.Fail(
                new AppError(403, "insufficient_role", "Only a GM can see who opened a book."));
        }

        var found = await _library.GetByIdAsync(documentId, worldId, role, ct);
        if (!found.IsSuccess)
        {
            return AppResult<IReadOnlyList<LibraryOpen>>.Fail(found.Error!);
        }

        var records = await _records.ListRecentAsync(documentId, OpensShown, ct);
        return AppResult<IReadOnlyList<LibraryOpen>>.Success(
            records.Select(r => new LibraryOpen(r.ViewerName, r.OpenedAt, r.PlayerId is not null)).ToList());
    }

    private static async Task<AppResult<LibraryReadLink>> DownloadAsync(Task<AppResult<LibraryDownload>> download)
    {
        var result = await download;
        return result.IsSuccess
            ? AppResult<LibraryReadLink>.Success(new LibraryReadLink(result.Value!.DownloadUrl, LibraryReadLink.Download))
            : AppResult<LibraryReadLink>.Fail(result.Error!);
    }

    private async Task<AppResult<LibraryReadLink>> MintAsync(
        Guid worldId, LibraryDocument document, string renewPath, string viewerName, int? startPage,
        LibraryReadingRecord record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.WebOrigin)
            || await _owners.ForWorldAsync(worldId, ct) is not { Connection: not null } owner)
        {
            return AppResult<LibraryReadLink>.Fail(new AppError(409, "bibliotaph_unavailable",
                "This book is in the GM's Bibliotaph, which isn't connected to this world right now."));
        }

        var name = string.IsNullOrWhiteSpace(viewerName) ? "A player" : viewerName.Trim();
        if (name.Length > LibraryReadingRecord.MaxViewerNameLength)
        {
            name = name[..LibraryReadingRecord.MaxViewerNameLength];
        }

        var request = new BibliotaphTicketRequest(
            document.BibliotaphDocumentId!,
            startPage is >= 0 ? startPage : null,
            name,
            _options.WebOrigin.TrimEnd('/') + renewPath);
        var ticket = await _client.MintReadingTicketAsync(owner.UserId, worldId, request, ct);
        if (!ticket.IsSuccess)
        {
            return AppResult<LibraryReadLink>.Fail(Refusal(ticket.Failure!.Value));
        }

        record.Id = Guid.NewGuid();
        record.WorldId = worldId;
        record.LibraryDocumentId = document.Id;
        record.ViewerName = name;
        record.OpenedAt = _time.GetUtcNow();
        await _records.AddAsync(record, ct);
        _logger.LogInformation(
            "Reading ticket minted for document {DocumentId} in world {WorldId} for {ViewerName}", document.Id, worldId, name);

        return AppResult<LibraryReadLink>.Success(new LibraryReadLink(ticket.Value!.Url, LibraryReadLink.Reader));
    }

    private static AppError Refusal(BibliotaphFailure failure) => failure switch
    {
        BibliotaphFailure.NotConnected or BibliotaphFailure.NoConnection => new AppError(409, "bibliotaph_needs_reconnect",
            "The GM's Bibliotaph needs reconnecting before this book can be opened."),
        BibliotaphFailure.Opening => new AppError(503, "bibliotaph_opening",
            "The GM's Bibliotaph is opening. Try again in a moment."),
        BibliotaphFailure.NotFound => new AppError(404, "not_found",
            "This book is no longer in the world's Bibliotaph collection."),
        _ => new AppError(502, "bibliotaph_unreachable", "Bibliotaph couldn't be reached. Try again in a moment."),
    };
}
