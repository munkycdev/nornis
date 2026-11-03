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
        AiCallScope scope, AiTask task, string nornisModel, bool needsImages, CancellationToken ct)
    {
        var holder = await KeyHolderAsync(scope, ct);

        if (holder is { } holderId)
        {
            var keys = await _keys.ListByUserAsync(holderId, ct);
            if (keys.Count > 0)
            {
                var choice = await _keys.GetChoiceAsync(holderId, task, ct);
                if (Pick(keys, choice, task, needsImages) is { } pick)
                {
                    if (pick.Key.CheckFailed)
                    {
                        throw new AiKeyRejectedException(pick.Key.Provider);
                    }

                    return new AiFunding(pick.Key.Provider, pick.Model, AiFundedBy.Key, pick.Key.Id, holderId,
                        pick.ModelId, pick.Price);
                }
            }
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

    private sealed record ModelPick(UserAiKey Key, string Model, string? ModelId, AiModelPrice Price);

    private ModelPick? Pick(IReadOnlyList<UserAiKey> keys, UserAiModelChoice? choice, AiTask task, bool needsImages)
    {
        if (choice is not null && Chosen(keys, choice.ModelId, task, needsImages) is { } chosen)
        {
            return chosen;
        }

        // A choice that no longer fits (the model left the catalog, its key was removed, or this
        // call needs images it can't read) falls back to the default, never to "off".
        foreach (var model in _options.Catalog)
        {
            if (Fits(model, task, needsImages) && KeyFor(keys, model.Provider) is { } key)
            {
                return new ModelPick(key, model.Model, model.Id, model.Price);
            }
        }

        return Compatible(keys, task, needsImages, lineId: null);
    }

    private ModelPick? Chosen(IReadOnlyList<UserAiKey> keys, string modelId, AiTask task, bool needsImages)
    {
        if (modelId.StartsWith(UserAiModelChoice.CompatiblePrefix, StringComparison.Ordinal))
        {
            return Guid.TryParse(modelId[UserAiModelChoice.CompatiblePrefix.Length..], out var lineId)
                ? Compatible(keys, task, needsImages, lineId)
                : null;
        }

        return _options.Find(modelId) is { } model && Fits(model, task, needsImages) && KeyFor(keys, model.Provider) is { } key
            ? new ModelPick(key, model.Model, model.Id, model.Price)
            : null;
    }

    private static ModelPick? Compatible(IReadOnlyList<UserAiKey> keys, AiTask task, bool needsImages, Guid? lineId)
    {
        if (KeyFor(keys, AiProvider.OpenAiCompatible) is not { } key)
        {
            return null;
        }

        var line = key.CompatibleModels.FirstOrDefault(m =>
            m.Task == task && (lineId is null || m.Id == lineId) && (!needsImages || m.SupportsImages));

        return line is null
            ? null
            : new ModelPick(key, line.ModelName, null, new AiModelPrice(line.InputPerMillionUsd, null, line.OutputPerMillionUsd));
    }

    private static bool Fits(AiCatalogModel model, AiTask task, bool needsImages) =>
        model.Tasks.Contains(task) && (!needsImages || model.SupportsImages);

    private static UserAiKey? KeyFor(IReadOnlyList<UserAiKey> keys, AiProvider provider) =>
        keys.FirstOrDefault(k => k.Provider == provider);

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
