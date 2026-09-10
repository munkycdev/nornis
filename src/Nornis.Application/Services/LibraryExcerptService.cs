using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly LibraryOptions _options;
    private readonly ILogger<LibraryExcerptService> _logger;

    public LibraryExcerptService(
        ILibraryService libraryService,
        ILibraryChunkRepository chunkRepository,
        IArtifactRepository artifactRepository,
        IReferencePassageRetriever passageRetriever,
        ISourceRepository sourceRepository,
        ISourceService sourceService,
        IOptions<LibraryOptions> options,
        ILogger<LibraryExcerptService> logger)
    {
        _libraryService = libraryService;
        _chunkRepository = chunkRepository;
        _artifactRepository = artifactRepository;
        _passageRetriever = passageRetriever;
        _sourceRepository = sourceRepository;
        _sourceService = sourceService;
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
            query, command.WorldId, LibraryService.GetAllowedScopes(command.ActingUserRole), command.ActingUserId, ct);

        IReadOnlyList<LibraryExcerptCandidate> candidates = passages
            .Select(p => new LibraryExcerptCandidate(p.ChunkId, p.DocumentId, p.DocumentTitle, p.Page, p.Text))
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
        if (document.Status != LibraryDocumentStatus.Indexed)
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

        var chunksResult = await ResolveChunksAsync(command, ct);
        if (!chunksResult.IsSuccess)
        {
            return AppResult<Source>.Fail(chunksResult.Error!);
        }

        var chunks = chunksResult.Value!;
        var pageFrom = chunks.Min(c => c.Page);
        var pageTo = chunks.Max(c => c.Page);
        var now = DateTimeOffset.UtcNow;

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Type = SourceType.LibraryExcerpt,
            Title = LibraryExcerptComposer.BuildTitle(artifactName, document.Title, pageFrom, pageTo),
            Body = LibraryExcerptComposer.BuildBody(artifactName, document.Title, pageFrom, pageTo, chunks, _options.OverlapChars),
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
            "Library excerpt filed. WorldId={WorldId}, DocumentId={DocumentId}, Pages={PageFrom}-{PageTo}, Chunks={Chunks}, SourceId={SourceId}, User={UserId}",
            command.WorldId, document.Id, pageFrom, pageTo, chunks.Count, source.Id, command.ActingUserId);

        return AppResult<Source>.Success(ready.Value!);
    }

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

    private static AppError TooLarge(int max) => new(400, "excerpt_too_large",
        $"An excerpt holds at most {max} passages. Narrow the page range or the selection and file the rest separately.");
}
