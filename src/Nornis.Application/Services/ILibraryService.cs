using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ILibraryService
{
    Task<AppResult<LibraryUploadTicket>> RequestUploadAsync(RequestLibraryUploadCommand command, CancellationToken ct);

    Task<AppResult<LibraryDocument>> ConfirmUploadAsync(Guid documentId, Guid worldId, Guid actingUserId, CancellationToken ct);

    Task<AppResult<IReadOnlyList<LibraryDocument>>> ListAsync(Guid worldId, WorldRole role, CancellationToken ct);

    Task<AppResult<LibraryDocument>> GetByIdAsync(Guid documentId, Guid worldId, WorldRole role, CancellationToken ct);

    Task<AppResult<LibraryDownload>> GetDownloadAsync(Guid documentId, Guid worldId, WorldRole role, CancellationToken ct);

    Task<AppResult<LibraryDocument>> SetVisibilityAsync(
        Guid documentId, Guid worldId, WorldRole role, VisibilityScope visibility, CancellationToken ct);

    Task<AppResult<bool>> DeleteAsync(Guid documentId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    Task<AppResult<LibraryDocument>> ReindexAsync(Guid documentId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
