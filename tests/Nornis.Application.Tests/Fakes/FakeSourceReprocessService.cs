using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Records reprocess commands. On success it mimics the real cascade's observable
/// contract for the replay: the source's status flips to Queued in the shared source
/// repository (so it drops out of the extractable-sources query), and the source is
/// returned. Ids in <see cref="FailingSourceIds"/> fail with invalid_status instead.
/// </summary>
public class FakeSourceReprocessService : ISourceReprocessService
{
    private readonly InMemorySourceRepository _sourceRepository;

    public FakeSourceReprocessService(InMemorySourceRepository sourceRepository)
    {
        _sourceRepository = sourceRepository;
    }

    public List<ReprocessSourceCommand> Commands { get; } = [];

    public HashSet<Guid> FailingSourceIds { get; } = [];

    public Exception? ThrowOnReprocess { get; set; }

    public Task<AppResult<ReprocessPreview>> PreviewAsync(
        Guid sourceId, Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        return Task.FromResult(AppResult<ReprocessPreview>.Success(
            new ReprocessPreview([], [], 0, 0, 0, 0)));
    }

    public async Task<AppResult<Source>> ReprocessAsync(ReprocessSourceCommand command, CancellationToken ct)
    {
        if (ThrowOnReprocess is not null)
        {
            throw ThrowOnReprocess;
        }

        Commands.Add(command);

        if (FailingSourceIds.Contains(command.SourceId))
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                "Only Processed or Failed sources can be reprocessed."));
        }

        var source = _sourceRepository.Sources.FirstOrDefault(s => s.Id == command.SourceId);
        if (source is null)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        await _sourceRepository.UpdateProcessingStatusAsync(source.Id, SourceProcessingStatus.Queued, ct);
        return AppResult<Source>.Success(source);
    }
}
