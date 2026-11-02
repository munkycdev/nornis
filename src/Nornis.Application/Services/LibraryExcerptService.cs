using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class LibraryExcerptService : ILibraryExcerptService
{
    private readonly ILibraryService _libraryService;
    private readonly ILibraryChunkRepository _chunkRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IReferencePassageRetriever _passageRetriever;
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceService _sourceService;
    private readonly IBibliotaphExcerptPages _bibliotaphPages;
    private readonly LibraryOptions _options;
    private readonly ILogger<LibraryExcerptService> _logger;

    public LibraryExcerptService(
        ILibraryService libraryService,
        ILibraryChunkRepository chunkRepository,
        IArtifactRepository artifactRepository,
        IReferencePassageRetriever passageRetriever,
        ISourceRepository sourceRepository,
        ISourceService sourceService,
        IBibliotaphExcerptPages bibliotaphPages,
        IOptions<LibraryOptions> options,
        ILogger<LibraryExcerptService> logger)
    {
        _libraryService = libraryService;
        _chunkRepository = chunkRepository;
        _artifactRepository = artifactRepository;
        _passageRetriever = passageRetriever;
        _sourceRepository = sourceRepository;
        _sourceService = sourceService;
        _bibliotaphPages = bibliotaphPages;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AppResult<IReadOnlyList<LibraryExcerptCandidate>>> SearchAsync(
        SearchLibraryExcerptsCommand command, CancellationToken ct)
    {
        if (GmOnly(command.ActingUserRole) is { } roleError)
        {
            return AppResult<IReadOnlyList<LibraryExcerptCandidate>>.Fail(roleError);
        }

        string query;
        if (!string.IsNullOrWhiteSpace(command.Query))
        {
            query = command.Query.Trim();
        }
        else if (command.ArtifactId is { } artifactId)
        {
            var artifact = await LoadArtifactAsync(artifactId, command.WorldId, ct);
            if (artifact is null)
            {
                return AppResult<IReadOnlyList<LibraryExcerptCandidate>>.Fail(NotFound("Artifact not found."));
            }

            query = string.IsNullOrWhiteSpace(artifact.Summary)
                ? artifact.Name
                : $"{artifact.Name}\n{artifact.Summary}";
        }
        else
        {
            return AppResult<IReadOnlyList<LibraryExcerptCandidate>>.Fail(
                new AppError(400, "validation_error", "Give a query or a codex entry to search for."));
        }

        var passages = await _passageRetriever.RetrieveForScopesAsync(
            query, command.WorldId, LibraryService.GetAllowedScopes(command.ActingUserRole), command.ActingUserId,
            ReferenceSearchMode.Search, ct);

        // An indexed chunk is filed by its id, a Bibliotaph page by its page number (feature 31).
        IReadOnlyList<LibraryExcerptCandidate> candidates = passages.Passages
            .Select(p => new LibraryExcerptCandidate(p.ChunkId, p.DocumentId, p.DocumentTitle, p.Page, p.Text, p.PageLabel))
            .ToList();

        return AppResult<IReadOnlyList<LibraryExcerptCandidate>>.Success(candidates);
    }

    public async Task<AppResult<Source>> FileAsync(FileLibraryExcerptCommand command, CancellationToken ct)
    {
        if (GmOnly(command.ActingUserRole) is { } roleError)
        {
            return AppResult<Source>.Fail(roleError);
        }

        var documentResult = await _libraryService.GetByIdAsync(command.DocumentId, command.WorldId, command.ActingUserRole, ct);
        if (!documentResult.IsSuccess)
        {
            return AppResult<Source>.Fail(documentResult.Error!);
        }

        var document = documentResult.Value!;
        var bibliotaphPages = BibliotaphPageNumbers(command, document);
        if (bibliotaphPages is null && document.Status != LibraryDocumentStatus.Indexed)
        {
            return AppResult<Source>.Fail(new AppError(409, "document_not_indexed",
                "Only an indexed document has passages to file. Wait for indexing to finish, or retry it."));
        }

        string? artifactName = null;
        if (command.ArtifactId is { } artifactId)
        {
            var artifact = await LoadArtifactAsync(artifactId, command.WorldId, ct);
            if (artifact is null)
            {
                return AppResult<Source>.Fail(NotFound("Artifact not found."));
            }

            artifactName = artifact.Name;
        }

        int pageFrom, pageTo, parts;
        string body;
        if (bibliotaphPages is not null)
        {
            var pagesResult = await _bibliotaphPages.ReadAsync(document, bibliotaphPages, ct);
            if (!pagesResult.IsSuccess)
            {
                return AppResult<Source>.Fail(pagesResult.Error!);
            }

            var pages = pagesResult.Value!;
            pageFrom = pages[0].Page;
            pageTo = pages[^1].Page;
            parts = pages.Count;
            body = LibraryExcerptComposer.BuildBody(artifactName, document.Title, pageFrom, pageTo, pages);
        }
        else
        {
            var chunksResult = await ResolveChunksAsync(command, ct);
            if (!chunksResult.IsSuccess)
            {
                return AppResult<Source>.Fail(chunksResult.Error!);
            }

            var chunks = chunksResult.Value!;
            pageFrom = chunks.Min(c => c.Page);
            pageTo = chunks.Max(c => c.Page);
            parts = chunks.Count;
            body = LibraryExcerptComposer.BuildBody(artifactName, document.Title, pageFrom, pageTo, chunks, _options.OverlapChars);
        }

        if (body.Length > SourceService.MaxBodyChars)
        {
            return AppResult<Source>.Fail(new AppError(400, "excerpt_too_large",
                $"The excerpt would be {body.Length:N0} characters; a source holds at most {SourceService.MaxBodyChars:N0}. Choose fewer pages."));
        }

        var now = DateTimeOffset.UtcNow;

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Type = SourceType.LibraryExcerpt,
            Title = LibraryExcerptComposer.BuildTitle(artifactName, document.Title, pageFrom, pageTo),
            Body = body,
            CreatedAt = now,
            CreatedByUserId = command.ActingUserId,
            // The document's shelf is the excerpt's audience: a GM-only sourcebook never
            // yields a party-visible source.
            Visibility = document.Visibility,
            ProcessingStatus = SourceProcessingStatus.Draft,
            ExtractionEnabled = true,
            LibraryDocumentId = document.Id,
            LibraryPageFrom = pageFrom,
            LibraryPageTo = pageTo
        };

        source = await _sourceRepository.CreateAsync(source, ct);

        // Queued through the one path every source takes to the queue — status ordering,
        // enqueue-failure revert and all. A failure here leaves the excerpt at Ready with the
        // same message a captured note would get, and the same retry from its page.
        var ready = await _sourceService.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, command.WorldId, command.ActingUserId, command.ActingUserRole), ct);
        if (!ready.IsSuccess)
        {
            return AppResult<Source>.Fail(ready.Error!);
        }

        _logger.LogInformation(
            "Library excerpt filed. WorldId={WorldId}, DocumentId={DocumentId}, Pages={PageFrom}-{PageTo}, Parts={Parts}, FromBibliotaph={FromBibliotaph}, SourceId={SourceId}, User={UserId}",
            command.WorldId, document.Id, pageFrom, pageTo, parts, bibliotaphPages is not null, source.Id, command.ActingUserId);

        return AppResult<Source>.Success(ready.Value!);
    }

    public async Task<AppResult<LibraryDocument>> FindBibliotaphDocumentAsync(
        Guid worldId, WorldRole role, string bibliotaphDocumentId, CancellationToken ct)
    {
        if (GmOnly(role) is { } roleError)
        {
            return AppResult<LibraryDocument>.Fail(roleError);
        }

        var row = await _bibliotaphPages.FindAsync(worldId, bibliotaphDocumentId, ct);
        return row is null
            ? AppResult<LibraryDocument>.Fail(NotFound("That book isn't in this world's Bibliotaph collection."))
            : AppResult<LibraryDocument>.Success(row);
    }

    public async Task<AppResult<IReadOnlyList<LibraryPage>>> ReadPagesAsync(
        Guid worldId, WorldRole role, Guid documentId, int pageFrom, int pageTo, CancellationToken ct)
    {
        if (GmOnly(role) is { } roleError)
        {
            return AppResult<IReadOnlyList<LibraryPage>>.Fail(roleError);
        }

        if (pageFrom < 1 || pageTo < pageFrom)
        {
            return AppResult<IReadOnlyList<LibraryPage>>.Fail(InvalidRange());
        }

        var documentResult = await _libraryService.GetByIdAsync(documentId, worldId, role, ct);
        if (!documentResult.IsSuccess)
        {
            return AppResult<IReadOnlyList<LibraryPage>>.Fail(documentResult.Error!);
        }

        return await _bibliotaphPages.ReadAsync(documentResult.Value!, PageRange(pageFrom, pageTo), ct);
    }

    /// <summary>
    /// The 1-based pages to read from Bibliotaph, or null for the indexed-chunk path. A book in
    /// the collection is read from Bibliotaph unless the GM ticked indexed chunks.
    /// </summary>
    private static IReadOnlyList<int>? BibliotaphPageNumbers(FileLibraryExcerptCommand command, LibraryDocument document)
    {
        if (document.BibliotaphDocumentId is null || command.ChunkIds.Count > 0)
        {
            return null;
        }

        if (command.Pages is { Count: > 0 } ticked)
        {
            return ticked;
        }

        // Nothing chosen, or a range that doesn't start at 1: ReadAsync refuses an empty list.
        return command.PageFrom is { } from && command.PageTo is { } to && from >= 1 && to >= from
            ? PageRange(from, to)
            : [];
    }

    /// <summary>
    /// A valid 1-based range as page numbers, cut one past the excerpt limit so a wild range
    /// costs nothing and is still refused as too large.
    /// </summary>
    private static IReadOnlyList<int> PageRange(int from, int to) =>
        Enumerable.Range(from, Math.Min(to - from + 1, BibliotaphExcerptPages.MaxSentPages + 1)).ToList();

    private async Task<AppResult<IReadOnlyList<LibraryChunkHit>>> ResolveChunksAsync(
        FileLibraryExcerptCommand command, CancellationToken ct)
    {
        var max = _options.MaxExcerptChunks;

        if (command.ChunkIds.Count > 0)
        {
            var ids = command.ChunkIds.Distinct().ToList();
            if (ids.Count > max)
            {
                return AppResult<IReadOnlyList<LibraryChunkHit>>.Fail(TooLarge(max));
            }

            var hits = await _chunkRepository.ListByIdsAsync(command.DocumentId, ids, ct);
            if (hits.Count != ids.Count)
            {
                return AppResult<IReadOnlyList<LibraryChunkHit>>.Fail(new AppError(400, "chunk_not_found",
                    "One or more of the chosen passages is not in this document."));
            }

            return AppResult<IReadOnlyList<LibraryChunkHit>>.Success(hits);
        }

        if (command.PageFrom is { } from && command.PageTo is { } to)
        {
            if (from < 1 || to < from)
            {
                return AppResult<IReadOnlyList<LibraryChunkHit>>.Fail(new AppError(400, "validation_error",
                    "Pages must start at 1 and the last page must not come before the first."));
            }

            var hits = await _chunkRepository.ListByDocumentPagesAsync(command.DocumentId, from, to, ct);
            if (hits.Count == 0)
            {
                return AppResult<IReadOnlyList<LibraryChunkHit>>.Fail(new AppError(400, "no_passages_in_range",
                    "No indexed passages start on those pages."));
            }

            if (hits.Count > max)
            {
                return AppResult<IReadOnlyList<LibraryChunkHit>>.Fail(TooLarge(max));
            }

            return AppResult<IReadOnlyList<LibraryChunkHit>>.Success(hits);
        }

        return AppResult<IReadOnlyList<LibraryChunkHit>>.Fail(new AppError(400, "validation_error",
            "Choose passages or a page range to file."));
    }

    private async Task<Artifact?> LoadArtifactAsync(Guid artifactId, Guid worldId, CancellationToken ct)
    {
        var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
        return artifact is null || artifact.WorldId != worldId ? null : artifact;
    }

    private static AppError? GmOnly(WorldRole role) =>
        role == WorldRole.GM
            ? null
            : new AppError(403, "insufficient_role", "Only GMs can file library excerpts.");

    private static AppError NotFound(string message) => new(404, "not_found", message);

    private static AppError InvalidRange() => new(400, "validation_error",
        "Pages must start at 1 and the last page must not come before the first.");

    private static AppError TooLarge(int max) => new(400, "excerpt_too_large",
        $"An excerpt holds at most {max} passages. Narrow the page range or the selection and file the rest separately.");
}
