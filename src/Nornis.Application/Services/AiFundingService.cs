using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// The funding rule (feature 39, design "Funding rule"). A world's calls spend its creator's keys;
/// a call with no world spends the keys of the user who caused it.
///
/// 1. The model is the key holder's choice for the task, or else the first catalog model, in
///    catalog order, whose provider they have a key for, or else their OpenAI-compatible line
///    for the task.
/// 2. That model's key pays, unless its last check or call was refused: then the call is refused
///    too. A broken key never quietly hands over to the trial; the GM has to know.
/// 3. With no key for the task, <see cref="AiModelsOptions.Fallback"/> decides: Nornis's own key
///    (until the cutover), or the trial while it has a balance and the month's ceiling has room.
///    Public visitors never spend the trial.
/// 4. Otherwise the call is refused with <see cref="AiKeyRequiredException"/>.
///
/// The budget guards and the global AI pause run before this, unchanged; funding never weakens
/// them. Scoped: a world's creator is looked up once per scope.
/// </summary>
public sealed class AiFundingService : IAiFundingService
{
    private readonly IAiKeyRepository _keys;
    private readonly IWorldRepository _worlds;
    private readonly IAiUsageRecordRepository _usage;
    private readonly AiModelsOptions _options;
    private readonly TimeProvider _time;
    private readonly Dictionary<Guid, Guid?> _creators = [];

    public AiFundingService(
        IAiKeyRepository keys,
        IWorldRepository worlds,
        IAiUsageRecordRepository usage,
        IOptions<AiModelsOptions> options,
        TimeProvider time)
    {
        _keys = keys;
        _worlds = worlds;
        _usage = usage;
        _options = options.Value;
        _time = time;
    }

    public async Task<AiFunding> ResolveAsync(
        AiCallScope scope, AiTask task, string? nornisModel, bool needsImages, CancellationToken ct)
    {
        var holder = await KeyHolderAsync(scope, ct);

        if (holder is { } holderId)
        {
            var keys = await _keys.ListByUserAsync(holderId, ct);
            if (keys.Count > 0)
            {
                var choice = await _keys.GetChoiceAsync(holderId, task, ct);
                if (AiModelPicker.Pick(_options, keys, choice?.ModelId, task, needsImages) is { } pick)
                {
                    if (pick.Key.CheckFailed)
                    {
                        throw new AiKeyRejectedException(pick.Key.Provider);
                    }

                    return new AiFunding(pick.Key.Provider, pick.Model, AiFundedBy.Key, pick.Key.Id, holderId,
                        pick.CatalogId, pick.Price);
                }
            }
        }

        // Nothing of Nornis's can run this work, so neither fallback applies: the trial runs on
        // Nornis's deployments too (feature 38's cover art).
        if (nornisModel is null)
        {
            throw new AiKeyRequiredException(task, "only the key holder's own key can run it.");
        }

        if (_options.Fallback == AiFallback.Nornis)
        {
            return new AiFunding(AiProvider.AzureOpenAi, nornisModel, AiFundedBy.Nornis, KeyHolderUserId: holder);
        }

        return await TrialAsync(scope, holder, task, nornisModel, ct);
    }

    private async Task<AiFunding> TrialAsync(
        AiCallScope scope, Guid? holder, AiTask task, string nornisModel, CancellationToken ct)
    {
        if (holder is not { } holderId || !scope.TrialAllowed)
        {
            throw new AiKeyRequiredException(task, "public questions need the world creator's own key.");
        }

        if (_options.TrialAllowanceUsd <= 0)
        {
            throw new AiKeyRequiredException(task, "there is no trial.");
        }

        // A call may take the balance below zero (it was under when it started); the next is refused.
        if (await _usage.SumTrialSpendAsync(holderId, ct) >= _options.TrialAllowanceUsd)
        {
            throw new AiKeyRequiredException(task, "the trial is used up.");
        }

        var now = _time.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (await _usage.SumAllTrialSpendAsync(monthStart, ct) >= _options.TrialMonthlyCeilingUsd)
        {
            throw new AiKeyRequiredException(task, "trials are paused until next month.");
        }

        return new AiFunding(AiProvider.AzureOpenAi, nornisModel, AiFundedBy.Trial, KeyHolderUserId: holderId);
    }

    private async Task<Guid?> KeyHolderAsync(AiCallScope scope, CancellationToken ct)
    {
        if (scope.WorldId is not { } worldId)
        {
            return scope.UserId;
        }

        if (!_creators.TryGetValue(worldId, out var creator))
        {
            creator = (await _worlds.GetByIdAsync(worldId, ct))?.CreatedByUserId;
            _creators[worldId] = creator;
        }

        return creator;
    }
}
