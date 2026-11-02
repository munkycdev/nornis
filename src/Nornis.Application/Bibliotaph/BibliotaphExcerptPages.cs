using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

public interface IBibliotaphExcerptPages
{
    /// <summary>
    /// The text of a book's pages in the world's Bibliotaph collection, in page order.
    /// <paramref name="pageNumbers"/> are 1-based, at most <see cref="BibliotaphOptions.MaxExcerptPages"/>
    /// of them. Who may ask is the caller's check; this is only the reading.
    /// </summary>
    Task<AppResult<IReadOnlyList<LibraryPage>>> ReadAsync(
        LibraryDocument document, IReadOnlyList<int> pageNumbers, CancellationToken ct);

    /// <summary>The world's live row for Bibliotaph's <paramref name="bibliotaphDocumentId"/>, after one reconcile if none is known.</summary>
    Task<LibraryDocument?> FindAsync(Guid worldId, string bibliotaphDocumentId, CancellationToken ct);
}

/// <summary>
/// Pages of a Bibliotaph book for an excerpt (feature 31, Requirement 7). Bibliotaph counts pages
/// from 0 and Nornis from 1; the conversion is <see cref="BibliotaphPages"/>. Ticked pages that
/// aren't next to each other are read as separate runs, so a GM ticking pp. 3 and 80 reads two
/// pages, not seventy-eight.
/// </summary>
public sealed class BibliotaphExcerptPages : IBibliotaphExcerptPages
{
    /// <summary>The widest range Bibliotaph's Send to Nornis may hand over; the GM trims it to <see cref="BibliotaphOptions.MaxExcerptPages"/>.</summary>
    public const int MaxSentPages = 40;

    private static readonly IReadOnlyList<VisibilityScope> EveryScope = Enum.GetValues<VisibilityScope>();

    private readonly IBibliotaphConnectionResolver _owners;
    private readonly IBibliotaphClient _client;
    private readonly ILibraryDocumentRepository _documents;
    private readonly IBibliotaphLibrarySync _sync;
    private readonly BibliotaphOptions _options;
    private readonly ILogger<BibliotaphExcerptPages> _logger;

    public BibliotaphExcerptPages(
        IBibliotaphConnectionResolver owners,
        IBibliotaphClient client,
        ILibraryDocumentRepository documents,
        IBibliotaphLibrarySync sync,
        IOptions<BibliotaphOptions> options,
        ILogger<BibliotaphExcerptPages> logger)
    {
        _owners = owners;
        _client = client;
        _documents = documents;
        _sync = sync;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AppResult<IReadOnlyList<LibraryPage>>> ReadAsync(
        LibraryDocument document, IReadOnlyList<int> pageNumbers, CancellationToken ct)
    {
        var pages = pageNumbers.Distinct().Order().ToList();
        if (pages.Count == 0 || pages[0] < 1)
        {
            return Fail(new AppError(400, "validation_error", "Choose pages to file; pages start at 1."));
        }

        if (pages.Count > _options.MaxExcerptPages)
        {
            return Fail(new AppError(400, "excerpt_too_large",
                $"An excerpt holds at most {_options.MaxExcerptPages} pages. Narrow the range and file the rest separately."));
        }

        if (document.BibliotaphDocumentId is null)
        {
            return Fail(new AppError(409, "not_in_bibliotaph", "This document isn't in the world's Bibliotaph collection."));
        }

        if (await _owners.ForWorldAsync(document.WorldId, ct) is not { Connection: not null } owner)
        {
            return Fail(new AppError(409, "bibliotaph_unavailable",
                "This book is in the GM's Bibliotaph, which isn't connected to this world right now."));
        }

        var read = new List<LibraryPage>();
        foreach (var (from, to) in Runs(pages))
        {
            var result = await _client.GetPagesAsync(owner.UserId, document.WorldId, document.BibliotaphDocumentId,
                BibliotaphPages.ToPageIndex(from), BibliotaphPages.ToPageIndex(to), ct);
            if (!result.IsSuccess)
            {
                _logger.LogInformation("Bibliotaph pages of document {DocumentId} unavailable ({Failure})", document.Id, result.Failure);
                return Fail(Refusal(result.Failure!.Value));
            }

            read.AddRange(result.Value!.Pages
                .Select(p => new LibraryPage(BibliotaphPages.ToPageNumber(p.PageIndex), p.PageLabel, p.Text))
                .Where(p => p.Page >= from && p.Page <= to));
        }

        IReadOnlyList<LibraryPage> ordered = read.DistinctBy(p => p.Page).OrderBy(p => p.Page).ToList();
        return ordered.Count == 0
            ? Fail(new AppError(400, "no_passages_in_range", "Bibliotaph has no text for those pages."))
            : AppResult<IReadOnlyList<LibraryPage>>.Success(ordered);
    }

    public async Task<LibraryDocument?> FindAsync(Guid worldId, string bibliotaphDocumentId, CancellationToken ct)
    {
        var row = await LookUpAsync(worldId, bibliotaphDocumentId, ct);
        if (row is not null)
        {
            return row;
        }

        await _sync.ReconcileAsync(worldId, ct);
        return await LookUpAsync(worldId, bibliotaphDocumentId, ct);
    }

    private async Task<LibraryDocument?> LookUpAsync(Guid worldId, string bibliotaphDocumentId, CancellationToken ct) =>
        (await _documents.ListByWorldAsync(worldId, EveryScope, ct))
            .FirstOrDefault(r => r.RemovedAt is null && string.Equals(r.BibliotaphDocumentId, bibliotaphDocumentId, StringComparison.Ordinal));

    /// <summary>Sorted page numbers as runs of consecutive pages.</summary>
    private static IEnumerable<(int From, int To)> Runs(IReadOnlyList<int> sorted)
    {
        var start = sorted[0];
        var previous = start;
        foreach (var page in sorted.Skip(1))
        {
            if (page != previous + 1)
            {
                yield return (start, previous);
                start = page;
            }

            previous = page;
        }

        yield return (start, previous);
    }

    private static AppResult<IReadOnlyList<LibraryPage>> Fail(AppError error) => AppResult<IReadOnlyList<LibraryPage>>.Fail(error);

    private static AppError Refusal(BibliotaphFailure failure) => failure switch
    {
        BibliotaphFailure.KeptFromAi => new AppError(403, "kept_from_ai",
            "This book is in a Bibliotaph folder kept from AI, so its pages can't be filed in Nornis."),
        BibliotaphFailure.NotConnected or BibliotaphFailure.NoConnection => new AppError(409, "bibliotaph_needs_reconnect",
            "Your Bibliotaph needs reconnecting on your Profile before its pages can be filed."),
        BibliotaphFailure.Opening => new AppError(503, "bibliotaph_opening",
            "Your Bibliotaph is opening. Try again in a moment."),
        BibliotaphFailure.NotFound => new AppError(404, "not_found",
            "This book is no longer in the world's Bibliotaph collection."),
        _ => new AppError(502, "bibliotaph_unreachable", "Bibliotaph couldn't be reached. Try again in a moment."),
    };
}
