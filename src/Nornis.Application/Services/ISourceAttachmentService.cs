using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ISourceAttachmentService
{
    Task<AppResult<SourceAttachmentUploadTicket>> RequestUploadAsync(RequestSourceAttachmentUploadCommand command, CancellationToken ct);

    Task<AppResult<SourceAttachment>> ConfirmUploadAsync(Guid attachmentId, Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    Task<AppResult<IReadOnlyList<SourceAttachmentWithUrl>>> ListAsync(Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    Task<AppResult<bool>> DeleteAsync(Guid attachmentId, Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
