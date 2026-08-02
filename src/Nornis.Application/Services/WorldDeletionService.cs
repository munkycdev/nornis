using Microsoft.Extensions.Logging;
using Nornis.Application.Authorization;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class WorldDeletionService : IWorldDeletionService
{
    private readonly IWorldRepository _worldRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<WorldDeletionService> _logger;

    public WorldDeletionService(
        IWorldRepository worldRepository,
        IWorldMemberRepository worldMemberRepository,
        IBlobStorageService blobStorageService,
        ILogger<WorldDeletionService> logger)
    {
        _worldRepository = worldRepository;
        _worldMemberRepository = worldMemberRepository;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<AppResult> DeleteAsync(DeleteWorldCommand command, CancellationToken ct)
    {
        var member = await _worldMemberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);

        if (member is null || !member.Role.IsAtLeast(WorldRole.GM))
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Only a GM can delete a world."));
        }

        var world = await _worldRepository.GetByIdAsync(command.WorldId, ct);

        if (world is null)
        {
            return AppResult.Fail(new AppError(404, "not_found", "World not found."));
        }

        // Case-sensitive on purpose: this is the last line of defense before an
        // unrecoverable wipe, so the typed name must match exactly.
        if (!string.Equals(command.ConfirmationName?.Trim(), world.Name, StringComparison.Ordinal))
        {
            return AppResult.Fail(new AppError(400, "confirmation_mismatch",
                "Type the world's exact name to confirm deletion."));
        }

        await _worldRepository.DeleteAsync(command.WorldId, ct);

        // Blob cleanup is best-effort after the DB wipe commits: a failure here must not
        // report the deletion as failed (the world is already gone), and the SAS-scoped
        // paths are unreachable once their rows are deleted.
        try
        {
            await _blobStorageService.DeleteByPrefixAsync($"worlds/{command.WorldId}/", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "World {WorldId} was deleted but its blobs under worlds/{WorldId}/ could not be removed",
                command.WorldId, command.WorldId);
        }

        _logger.LogInformation(
            "World {WorldId} (\"{WorldName}\") permanently deleted by user {UserId}",
            command.WorldId, world.Name, command.ActingUserId);

        return AppResult.Success();
    }
}
