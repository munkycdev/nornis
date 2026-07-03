using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class SourceService : ISourceService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly ICampaignMemberRepository _campaignMemberRepository;
    private readonly IExtractionQueueClient _extractionQueueClient;

    private static readonly Dictionary<SourceProcessingStatus, HashSet<SourceProcessingStatus>> ValidTransitions = new()
    {
        [SourceProcessingStatus.Draft] = new() { SourceProcessingStatus.Ready },
        [SourceProcessingStatus.Ready] = new() { SourceProcessingStatus.Queued },
        [SourceProcessingStatus.Queued] = new() { SourceProcessingStatus.Processing },
        [SourceProcessingStatus.Processing] = new() { SourceProcessingStatus.Processed, SourceProcessingStatus.Failed },
        [SourceProcessingStatus.Processed] = new(),
        [SourceProcessingStatus.Failed] = new() { SourceProcessingStatus.Ready },
    };

    public SourceService(
        ISourceRepository sourceRepository,
        ICampaignMemberRepository campaignMemberRepository,
        IExtractionQueueClient extractionQueueClient)
    {
        _sourceRepository = sourceRepository;
        _campaignMemberRepository = campaignMemberRepository;
        _extractionQueueClient = extractionQueueClient;
    }

    public async Task<AppResult<Source>> CreateAsync(CreateSourceCommand command, CancellationToken ct)
    {
        // Role enforcement: Observer cannot create
        if (command.CreatingUserRole == CampaignRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot create sources."));
        }

        // Input validation
        var titleError = ValidateTitle(command.Title);
        if (titleError is not null)
        {
            return AppResult<Source>.Fail(titleError);
        }

        var bodyError = ValidateBody(command.Body);
        if (bodyError is not null)
        {
            return AppResult<Source>.Fail(bodyError);
        }

        var uriError = ValidateUri(command.Uri);
        if (uriError is not null)
        {
            return AppResult<Source>.Fail(uriError);
        }

        // Player cannot set GMOnly visibility
        if (command.CreatingUserRole == CampaignRole.Player && command.Visibility == VisibilityScope.GMOnly)
        {
            return AppResult<Source>.Fail(new AppError(400, "validation_error", "Players cannot create GMOnly sources."));
        }

        var now = DateTimeOffset.UtcNow;

        var source = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = command.CampaignId,
            Type = command.Type,
            Title = command.Title,
            Body = command.Body,
            Uri = command.Uri,
            OccurredAt = command.OccurredAt,
            CreatedAt = now,
            CreatedByUserId = command.CreatingUserId,
            Visibility = command.Visibility,
            ProcessingStatus = SourceProcessingStatus.Draft
        };

        source = await _sourceRepository.CreateAsync(source, ct);

        return AppResult<Source>.Success(source);
    }

    public async Task<AppResult<Source>> GetByIdAsync(Guid sourceId, Guid campaignId, Guid requestingUserId, CampaignRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null || source.CampaignId != campaignId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Visibility enforcement — return not-found for invisible sources
        if (!CanSeeSource(source, requestingUserId, role))
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        return AppResult<Source>.Success(source);
    }

    public async Task<AppResult<Source>> UpdateAsync(UpdateSourceCommand command, CancellationToken ct)
    {
        // Role enforcement: Observer cannot update
        if (command.ActingUserRole == CampaignRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot update sources."));
        }

        var source = await _sourceRepository.GetByIdAsync(command.SourceId, ct);

        if (source is null || source.CampaignId != command.CampaignId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Ownership enforcement: only creator or GM can update
        if (source.CreatedByUserId != command.ActingUserId && command.ActingUserRole != CampaignRole.GM)
        {
            return AppResult<Source>.Fail(new AppError(403, "forbidden", "Only the source creator or a GM can update this source."));
        }

        // Processing status guards: updates blocked when Queued/Processing/Processed
        if (source.ProcessingStatus is SourceProcessingStatus.Queued
            or SourceProcessingStatus.Processing
            or SourceProcessingStatus.Processed)
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                $"Source cannot be modified while in {source.ProcessingStatus} status."));
        }

        // Validate optional Title if provided
        if (command.Title is not null)
        {
            var titleError = ValidateTitle(command.Title);
            if (titleError is not null)
            {
                return AppResult<Source>.Fail(titleError);
            }

            source.Title = command.Title;
        }

        // Validate optional Body if provided
        if (command.Body is not null)
        {
            var bodyError = ValidateBody(command.Body);
            if (bodyError is not null)
            {
                return AppResult<Source>.Fail(bodyError);
            }

            source.Body = command.Body;
        }

        // Validate optional Uri if provided
        if (command.Uri is not null)
        {
            var uriError = ValidateUri(command.Uri);
            if (uriError is not null)
            {
                return AppResult<Source>.Fail(uriError);
            }

            source.Uri = command.Uri;
        }

        if (command.OccurredAt is not null)
        {
            source.OccurredAt = command.OccurredAt;
        }

        if (command.Type is not null)
        {
            source.Type = command.Type.Value;
        }

        if (command.Visibility is not null)
        {
            // Player cannot set GMOnly visibility
            if (command.ActingUserRole == CampaignRole.Player && command.Visibility == VisibilityScope.GMOnly)
            {
                return AppResult<Source>.Fail(new AppError(400, "validation_error", "Players cannot set GMOnly visibility."));
            }

            source.Visibility = command.Visibility.Value;
        }

        source = await _sourceRepository.UpdateAsync(source, ct);

        return AppResult<Source>.Success(source);
    }

    public async Task<AppResult> DeleteAsync(Guid sourceId, Guid campaignId, Guid actingUserId, CampaignRole role, CancellationToken ct)
    {
        // Role enforcement: Observer cannot delete
        if (role == CampaignRole.Observer)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Observers cannot delete sources."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null || source.CampaignId != campaignId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Ownership enforcement: only creator or GM can delete
        if (source.CreatedByUserId != actingUserId && role != CampaignRole.GM)
        {
            return AppResult.Fail(new AppError(403, "forbidden", "Only the source creator or a GM can delete this source."));
        }

        // Processing status guards: deletes blocked when Queued/Processing
        if (source.ProcessingStatus is SourceProcessingStatus.Queued or SourceProcessingStatus.Processing)
        {
            return AppResult.Fail(new AppError(409, "invalid_status",
                $"Source cannot be deleted while in {source.ProcessingStatus} status."));
        }

        await _sourceRepository.DeleteAsync(sourceId, ct);

        return AppResult.Success();
    }

    public async Task<AppResult<IReadOnlyList<Source>>> ListByCampaignAsync(Guid campaignId, Guid requestingUserId, CampaignRole role, CancellationToken ct)
    {
        var allSources = await _sourceRepository.ListByCampaignAsync(campaignId, cancellationToken: ct);

        var visibleSources = allSources
            .Where(s => CanSeeSource(s, requestingUserId, role))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();

        return AppResult<IReadOnlyList<Source>>.Success(visibleSources);
    }

    public async Task<AppResult<Source>> MarkReadyAsync(MarkSourceReadyCommand command, CancellationToken ct)
    {
        // Role enforcement: Observer cannot mark ready
        if (command.ActingUserRole == CampaignRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot mark sources as ready."));
        }

        var source = await _sourceRepository.GetByIdAsync(command.SourceId, ct);

        if (source is null || source.CampaignId != command.CampaignId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Ownership enforcement: only creator or GM can mark ready
        if (source.CreatedByUserId != command.ActingUserId && command.ActingUserRole != CampaignRole.GM)
        {
            return AppResult<Source>.Fail(new AppError(403, "forbidden", "Only the source creator or a GM can mark this source as ready."));
        }

        // State machine: only Draft can transition to Ready
        if (!IsValidTransition(source.ProcessingStatus, SourceProcessingStatus.Ready))
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_transition",
                $"Cannot transition from {source.ProcessingStatus} to Ready."));
        }

        // Transition Draft → Ready
        source.ProcessingStatus = SourceProcessingStatus.Ready;
        source = await _sourceRepository.UpdateAsync(source, ct);

        // Attempt to enqueue extraction message
        try
        {
            await _extractionQueueClient.SendExtractionMessageAsync(source.Id, source.CampaignId, ct);
        }
        catch
        {
            // Failed enqueue: leave at Ready and return error
            return AppResult<Source>.Fail(new AppError(502, "enqueue_failed",
                "Failed to enqueue source for extraction. The source remains at Ready status."));
        }

        // Enqueue succeeded: transition Ready → Queued
        source.ProcessingStatus = SourceProcessingStatus.Queued;
        source = await _sourceRepository.UpdateAsync(source, ct);

        return AppResult<Source>.Success(source);
    }

    private static bool CanSeeSource(Source source, Guid userId, CampaignRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == CampaignRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == CampaignRole.GM,
        _ => false
    };

    private static bool IsValidTransition(SourceProcessingStatus current, SourceProcessingStatus target)
    {
        return ValidTransitions.TryGetValue(current, out var validTargets) && validTargets.Contains(target);
    }

    private static AppError? ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new AppError(400, "validation_error", "Source title must not be empty or whitespace.");
        }

        if (title.Length > 200)
        {
            return new AppError(400, "validation_error", "Source title must be between 1 and 200 characters.");
        }

        return null;
    }

    private static AppError? ValidateBody(string? body)
    {
        if (body is not null && body.Length > 100_000)
        {
            return new AppError(400, "validation_error", "Source body must not exceed 100,000 characters.");
        }

        return null;
    }

    private static AppError? ValidateUri(string? uri)
    {
        if (uri is not null && uri.Length > 2_048)
        {
            return new AppError(400, "validation_error", "Source URI must not exceed 2,048 characters.");
        }

        return null;
    }
}
