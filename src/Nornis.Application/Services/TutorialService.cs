using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Onboarding flags and the demo-world tutorial checklist (feature 20 phase C). Completion
/// truth for state-backed steps lives in world state; <see cref="TutorialProgress"/> rows
/// cache detections so completion is sticky and detector queries don't re-run forever.
/// </summary>
public class TutorialService : ITutorialService
{
    private const string SessionSixEntryName = "tutorial/session-6.md";

    private static readonly AppError TutorialUnavailable =
        new(404, "tutorial_unavailable", "This world has no tutorial.");

    private readonly IUserRepository _userRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly ITutorialProgressRepository _progressRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewProposalRepository _proposalRepository;
    private readonly IReviewBatchRepository _batchRepository;
    private readonly IAiUsageRecordRepository _aiUsageRepository;
    private readonly IDemoWorldTemplateProvider _templateProvider;
    private readonly ILogger<TutorialService> _logger;

    public TutorialService(
        IUserRepository userRepository,
        IWorldRepository worldRepository,
        ITutorialProgressRepository progressRepository,
        ISourceRepository sourceRepository,
        IReviewProposalRepository proposalRepository,
        IReviewBatchRepository batchRepository,
        IAiUsageRecordRepository aiUsageRepository,
        IDemoWorldTemplateProvider templateProvider,
        ILogger<TutorialService> logger)
    {
        _userRepository = userRepository;
        _worldRepository = worldRepository;
        _progressRepository = progressRepository;
        _sourceRepository = sourceRepository;
        _proposalRepository = proposalRepository;
        _batchRepository = batchRepository;
        _aiUsageRepository = aiUsageRepository;
        _templateProvider = templateProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------ onboarding --

    public async Task<OnboardingState> GetOnboardingAsync(Guid userId, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        return new OnboardingState(
            PromptSeen: user?.OnboardingPromptSeenAt is not null,
            TutorialDismissed: user?.TutorialDismissedAt is not null);
    }

    public Task MarkPromptSeenAsync(Guid userId, CancellationToken ct) =>
        StampUserAsync(userId, u => u.OnboardingPromptSeenAt ??= DateTimeOffset.UtcNow, ct);

    public Task DismissTutorialAsync(Guid userId, CancellationToken ct) =>
        StampUserAsync(userId, u => u.TutorialDismissedAt ??= DateTimeOffset.UtcNow, ct);

    private async Task StampUserAsync(Guid userId, Action<User> stamp, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return;
        }

        stamp(user);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);
    }

    // ------------------------------------------------------------------- checklist --

    public async Task<AppResult<TutorialChecklist>> GetChecklistAsync(Guid worldId, Guid userId, CancellationToken ct)
    {
        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        if (world is not { IsDemo: true, TutorialEnabled: true })
        {
            return AppResult<TutorialChecklist>.Fail(TutorialUnavailable);
        }

        var cached = (await _progressRepository.ListAsync(userId, worldId, ct))
            .ToDictionary(p => p.StepKey, p => p.CompletedAt);

        var fresh = new List<TutorialProgress>();
        foreach (var step in TutorialSteps.All.Where(s => !s.ClientReported && !cached.ContainsKey(s.Key)))
        {
            if (await DetectAsync(step.Key, world, userId, ct))
            {
                fresh.Add(new TutorialProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    WorldId = worldId,
                    StepKey = step.Key,
                    CompletedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        if (fresh.Count > 0)
        {
            await _progressRepository.AddRangeAsync(fresh, ct);
            fresh.ForEach(p => cached[p.StepKey] = p.CompletedAt);
            _logger.LogInformation("Tutorial steps detected complete for user {UserId} in world {WorldId}: {Steps}",
                userId, worldId, string.Join(",", fresh.Select(p => p.StepKey)));
        }

        return AppResult<TutorialChecklist>.Success(ToChecklist(cached));
    }

    public async Task<AppResult<TutorialChecklist>> ReportStepAsync(Guid worldId, Guid userId, string stepKey, CancellationToken ct)
    {
        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        if (world is not { IsDemo: true, TutorialEnabled: true })
        {
            return AppResult<TutorialChecklist>.Fail(TutorialUnavailable);
        }

        var definition = TutorialSteps.All.FirstOrDefault(s => s.Key == stepKey);
        if (definition is null || !definition.ClientReported)
        {
            return AppResult<TutorialChecklist>.Fail(new AppError(400, "invalid_step",
                "That step is not one the client may report."));
        }

        var cached = (await _progressRepository.ListAsync(userId, worldId, ct))
            .ToDictionary(p => p.StepKey, p => p.CompletedAt);

        // The chapter-two closer only counts after there is a reveal to go look at.
        if (stepKey == TutorialSteps.SeeWhatTheySee
            && !cached.ContainsKey(TutorialSteps.RevealSecret)
            && !await DetectAsync(TutorialSteps.RevealSecret, world, userId, ct))
        {
            return AppResult<TutorialChecklist>.Fail(new AppError(409, "reveal_first",
                "Reveal a secret before checking what the players see."));
        }

        if (!cached.ContainsKey(stepKey))
        {
            var entry = new TutorialProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WorldId = worldId,
                StepKey = stepKey,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            await _progressRepository.AddRangeAsync([entry], ct);
            cached[stepKey] = entry.CompletedAt;
        }

        return AppResult<TutorialChecklist>.Success(ToChecklist(cached));
    }

    public async Task<AppResult<string>> GetSessionSixAsync(CancellationToken ct)
    {
        if (!_templateProvider.IsAvailable)
        {
            return AppResult<string>.Fail(new AppError(404, "not_found", "No session text is available."));
        }

        await using var stream = _templateProvider.OpenRead();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.GetEntry(SessionSixEntryName) is not { } entry)
        {
            return AppResult<string>.Fail(new AppError(404, "not_found", "No session text is available."));
        }

        using var reader = new StreamReader(entry.Open());
        var text = await reader.ReadToEndAsync(ct);
        return AppResult<string>.Success(text);
    }

    // ------------------------------------------------------------------- detectors --

    private async Task<bool> DetectAsync(string stepKey, World world, Guid userId, CancellationToken ct)
    {
        switch (stepKey)
        {
            // Every detector is an existence check pushed into SQL. The checklist polls this
            // whole switch every 15 seconds for the duration of a new user's first session, and
            // it used to answer four booleans by loading four tables in full — two of them
            // carrying session bodies. AnyDecidedByWorldAsync below was already the right shape;
            // the others now match it.
            case TutorialSteps.AskTheLoremaster:
                return await _aiUsageRepository.AnySucceededAsync(
                    world.Id, userId, AiOperationType.AskLoremaster, ct);

            // Template rows are all written with CreatedAt == world.CreatedAt, so anything
            // strictly later is the user's own addition.
            case TutorialSteps.AddSessionSix:
                return await _sourceRepository.AnyCreatedAfterAsync(world.Id, world.CreatedAt, null, ct);

            case TutorialSteps.WatchExtraction:
                return await _sourceRepository.AnyCreatedAfterAsync(
                    world.Id, world.CreatedAt, SourceProcessingStatus.Processed, ct);

            case TutorialSteps.VetExtraction:
                return await _proposalRepository.AnyDecidedByWorldAsync(world.Id, ct);

            case TutorialSteps.RevealSecret:
                // Case-sensitive where the old in-memory check was not. Kind is never
                // user-supplied, so this matches the same rows RevealService writes — and a
                // SQL-side comparison keeps it a single indexed predicate rather than a
                // full scan.
                return await _batchRepository.AnyOfKindAsync(world.Id, ReviewBatchKinds.Reveal, ct);

            default:
                return false;
        }
    }

    private static TutorialChecklist ToChecklist(IReadOnlyDictionary<string, DateTimeOffset> completed) =>
        new(TutorialSteps.All
            .Select(s => new TutorialStepState(
                s.Key, s.Chapter, s.ClientReported,
                completed.TryGetValue(s.Key, out var at) ? at : null))
            .ToList());
}
