using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

/// <summary>One page of a provider listing; <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record BibliotaphProviderPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public interface IBibliotaphProviderService
{
    Task<AppResult<BibliotaphProviderPage<World>>> ListWorldsAsync(Guid userId, string? cursor, CancellationToken ct);

    Task<AppResult<BibliotaphProviderPage<LibraryDocument>>> ListFilesAsync(
        Guid userId, Guid worldId, string? cursor, CancellationToken ct);

    Task<AppResult<BlobReadLink>> GetLinkAsync(Guid userId, Guid fileId, CancellationToken ct);

    /// <summary>Queues a <c>created</c> event for each of the user's worlds. Nothing to queue without a connection.</summary>
    Task<int> ResyncAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// What Nornis provides to Bibliotaph's Nornis storage provider (feature 31, Requirement 5): the
/// caller's own worlds and their exposed uploads, read-only. A world the caller didn't create,
/// a demo or template world, or a file that isn't exposed is a 404, the same answer as one that
/// doesn't exist, so the provider learns nothing about other people's worlds.
/// </summary>
public sealed class BibliotaphProviderService : IBibliotaphProviderService
{
    public const int PageSize = 100;

    private readonly IBibliotaphProviderRepository _provider;
    private readonly IBibliotaphConnectionRepository _connections;
    private readonly IBibliotaphOutboxRepository _outbox;
    private readonly IBlobStorageService _blobs;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;

    public BibliotaphProviderService(
        IBibliotaphProviderRepository provider,
        IBibliotaphConnectionRepository connections,
        IBibliotaphOutboxRepository outbox,
        IBlobStorageService blobs,
        IOptions<BibliotaphOptions> options,
        TimeProvider time)
    {
        _provider = provider;
        _connections = connections;
        _outbox = outbox;
        _blobs = blobs;
        _options = options.Value;
        _time = time;
    }

    public async Task<AppResult<BibliotaphProviderPage<World>>> ListWorldsAsync(Guid userId, string? cursor, CancellationToken ct)
    {
        if (!BibliotaphCursor.TryRead(cursor, out var after))
        {
            return AppResult<BibliotaphProviderPage<World>>.Fail(BadCursor);
        }

        var rows = await _provider.ListWorldsAsync(userId, after, PageSize + 1, ct);
        return AppResult<BibliotaphProviderPage<World>>.Success(Page(rows, w => w.Id));
    }

    public async Task<AppResult<BibliotaphProviderPage<LibraryDocument>>> ListFilesAsync(
        Guid userId, Guid worldId, string? cursor, CancellationToken ct)
    {
        if (!BibliotaphCursor.TryRead(cursor, out var after))
        {
            return AppResult<BibliotaphProviderPage<LibraryDocument>>.Fail(BadCursor);
        }

        if (await _provider.GetWorldAsync(userId, worldId, ct) is null)
        {
            return AppResult<BibliotaphProviderPage<LibraryDocument>>.Fail(NotFound("World not found."));
        }

        var rows = await _provider.ListFilesAsync(worldId, after, PageSize + 1, ct);
        return AppResult<BibliotaphProviderPage<LibraryDocument>>.Success(Page(rows, d => d.Id));
    }

    public async Task<AppResult<BlobReadLink>> GetLinkAsync(Guid userId, Guid fileId, CancellationToken ct)
    {
        var file = await _provider.GetFileAsync(userId, fileId, ct);
        if (file is null)
        {
            return AppResult<BlobReadLink>.Fail(NotFound("File not found."));
        }

        var link = await _blobs.GenerateReadLinkAsync(file.BlobPath, TimeSpan.FromMinutes(_options.FileLinkMinutes), ct);
        return AppResult<BlobReadLink>.Success(link);
    }

    public async Task<int> ResyncAsync(Guid userId, CancellationToken ct)
    {
        // The outbox holds rows only for connected creators: with no connection there is no one
        // to deliver them, and the backfill on connect does this anyway.
        if (await _connections.GetByUserIdAsync(userId, ct) is null)
        {
            return 0;
        }

        return await _outbox.QueueCreatedForWorldsOfAsync(userId, _time.GetUtcNow(), ct);
    }

    private static BibliotaphProviderPage<T> Page<T>(IReadOnlyList<T> rows, Func<T, Guid> id)
    {
        if (rows.Count <= PageSize)
        {
            return new BibliotaphProviderPage<T>(rows, null);
        }

        var page = rows.Take(PageSize).ToList();
        return new BibliotaphProviderPage<T>(page, BibliotaphCursor.Write(id(page[^1])));
    }

    private static readonly AppError BadCursor = new(400, "bad_cursor", "The cursor isn't one Nornis issued.");

    private static AppError NotFound(string message) => new(404, "not_found", message);
}

/// <summary>The provider listings' opaque cursor: the last id of the page, base64url.</summary>
public static class BibliotaphCursor
{
    public static string Write(Guid lastId) => Convert.ToBase64String(lastId.ToByteArray())
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>True for no cursor (the first page, <paramref name="after"/> null) or one <see cref="Write"/> made.</summary>
    public static bool TryRead(string? cursor, out Guid? after)
    {
        after = null;
        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        if (cursor.Length != 22)
        {
            return false;
        }

        var bytes = new byte[16];
        var base64 = cursor.Replace('-', '+').Replace('_', '/') + "==";
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != 16)
        {
            return false;
        }

        after = new Guid(bytes);
        return true;
    }
}
