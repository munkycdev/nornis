using Nornis.Application.Errors;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Application.Bibliotaph;

public interface IBibliotaphCovers
{
    Task<AppResult<BibliotaphCover>> GetAsync(Guid worldId, Guid documentId, WorldRole role, CancellationToken ct);
}

/// <summary>
/// A Bibliotaph book's cover (feature 31, Requirement 3), streamed through Nornis so the browser
/// never holds a Bibliotaph token. Whoever may see the row may see its cover: the Library's own
/// visibility check decides, then the world creator's connection fetches it.
/// </summary>
public sealed class BibliotaphCovers : IBibliotaphCovers
{
    private readonly ILibraryService _library;
    private readonly IBibliotaphConnectionResolver _owners;
    private readonly IBibliotaphClient _client;

    public BibliotaphCovers(ILibraryService library, IBibliotaphConnectionResolver owners, IBibliotaphClient client)
    {
        _library = library;
        _owners = owners;
        _client = client;
    }

    public async Task<AppResult<BibliotaphCover>> GetAsync(Guid worldId, Guid documentId, WorldRole role, CancellationToken ct)
    {
        var document = await _library.GetByIdAsync(documentId, worldId, role, ct);
        if (!document.IsSuccess)
        {
            return AppResult<BibliotaphCover>.Fail(document.Error!);
        }

        if (document.Value!.CoverName is not { } coverName
            || await _owners.ForWorldAsync(worldId, ct) is not { Connection: not null } owner)
        {
            return AppResult<BibliotaphCover>.Fail(NoCover);
        }

        var cover = await _client.GetCoverAsync(owner.UserId, coverName, ct);
        return cover.IsSuccess
            ? AppResult<BibliotaphCover>.Success(cover.Value!)
            : AppResult<BibliotaphCover>.Fail(NoCover);
    }

    private static readonly AppError NoCover = new(404, "no_cover", "This book has no cover to show.");
}
