using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Step 1 of the funding rule (feature 39): which of the key holder's models runs a task. Shared by
/// <see cref="AiFundingService"/>, which spends it, and Profile's Models card, which shows it as
/// the default, so the two can't disagree.
/// </summary>
internal static class AiModelPicker
{
    /// <param name="Key">The key that pays.</param>
    /// <param name="Model">The provider's model name, as sent on the wire.</param>
    /// <param name="CatalogId">The catalog id; null for an OpenAI-compatible line.</param>
    /// <param name="ChoiceId">What a <see cref="UserAiModelChoice"/> naming this model stores.</param>
    internal sealed record ModelPick(UserAiKey Key, string Model, string? CatalogId, string ChoiceId, AiModelPrice Price);

    /// <summary>
    /// The chosen model when it still fits, otherwise the first catalog model, in catalog order,
    /// whose provider the holder has a key for, otherwise their OpenAI-compatible line for the
    /// task; null when they have nothing for it. A key whose check failed still counts: the
    /// caller refuses it rather than quietly moving on.
    /// </summary>
    internal static ModelPick? Pick(
        AiModelsOptions options, IReadOnlyList<UserAiKey> keys, string? choiceId, AiTask task, bool needsImages)
    {
        if (choiceId is not null && Chosen(options, keys, choiceId, task, needsImages) is { } chosen)
        {
            return chosen;
        }

        // A choice that no longer fits (the model left the catalog, its key was removed, or this
        // call needs images it can't read) falls back to the default, never to "off".
        foreach (var model in options.Catalog)
        {
            if (Fits(model, task, needsImages) && KeyFor(keys, model.Provider) is { } key)
            {
                return new ModelPick(key, model.Model, model.Id, model.Id, model.Price);
            }
        }

        return Compatible(keys, task, needsImages, lineId: null);
    }

    private static ModelPick? Chosen(
        AiModelsOptions options, IReadOnlyList<UserAiKey> keys, string choiceId, AiTask task, bool needsImages)
    {
        if (choiceId.StartsWith(UserAiModelChoice.CompatiblePrefix, StringComparison.Ordinal))
        {
            return Guid.TryParse(choiceId[UserAiModelChoice.CompatiblePrefix.Length..], out var lineId)
                ? Compatible(keys, task, needsImages, lineId)
                : null;
        }

        return options.Find(choiceId) is { } model && Fits(model, task, needsImages) && KeyFor(keys, model.Provider) is { } key
            ? new ModelPick(key, model.Model, model.Id, model.Id, model.Price)
            : null;
    }

    private static ModelPick? Compatible(IReadOnlyList<UserAiKey> keys, AiTask task, bool needsImages, Guid? lineId)
    {
        // An OpenAI-compatible endpoint is a chat endpoint: no line of one makes images (feature 38).
        if (AiTasks.OnlyKeysRun(task) || KeyFor(keys, AiProvider.OpenAiCompatible) is not { } key)
        {
            return null;
        }

        var line = key.CompatibleModels.FirstOrDefault(m =>
            m.Task == task && (lineId is null || m.Id == lineId) && (!needsImages || m.SupportsImages));

        return line is null
            ? null
            : new ModelPick(key, line.ModelName, null, CompatibleId(line.Id),
                new AiModelPrice(line.InputPerMillionUsd, null, line.OutputPerMillionUsd));
    }

    internal static string CompatibleId(Guid lineId) => UserAiModelChoice.CompatiblePrefix + lineId;

    internal static bool Fits(AiCatalogModel model, AiTask task, bool needsImages) =>
        model.Tasks.Contains(task) && (!needsImages || model.SupportsImages);

    private static UserAiKey? KeyFor(IReadOnlyList<UserAiKey> keys, AiProvider provider) =>
        keys.FirstOrDefault(k => k.Provider == provider);
}
