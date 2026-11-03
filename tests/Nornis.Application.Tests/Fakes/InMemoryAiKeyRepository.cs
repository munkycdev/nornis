using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryAiKeyRepository : IAiKeyRepository
{
    public List<UserAiKey> Keys { get; } = [];

    public List<UserAiModelChoice> Choices { get; } = [];

    public Task<IReadOnlyList<UserAiKey>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserAiKey>>(Keys.Where(k => k.UserId == userId).OrderBy(k => k.AddedAt).ToList());

    public Task<UserAiKey?> GetAsync(Guid keyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Keys.FirstOrDefault(k => k.Id == keyId));

    public Task<UserAiModelChoice?> GetChoiceAsync(Guid userId, AiTask task, CancellationToken cancellationToken = default) =>
        Task.FromResult(Choices.FirstOrDefault(c => c.UserId == userId && c.Task == task));

    public Task<IReadOnlyList<UserAiModelChoice>> ListChoicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserAiModelChoice>>(Choices.Where(c => c.UserId == userId).ToList());

    public Task MarkCheckFailedAsync(Guid keyId, DateTimeOffset keyVersion, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        foreach (var key in Keys.Where(k => k.Id == keyId && k.UpdatedAt == keyVersion))
        {
            key.CheckFailed = true;
            key.CheckedAt = at;
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(UserAiKey key, CancellationToken cancellationToken = default)
    {
        var stored = Keys.FirstOrDefault(k => k.Id == key.Id);
        if (stored is not null)
        {
            // As the real one: a line keeps its id while its task is still listed.
            foreach (var line in key.CompatibleModels)
            {
                if (stored.CompatibleModels.FirstOrDefault(l => l.Task == line.Task) is { } kept)
                {
                    line.Id = kept.Id;
                }
            }

            Keys.Remove(stored);
        }

        foreach (var line in key.CompatibleModels)
        {
            line.UserAiKeyId = key.Id;
        }

        Keys.Add(key);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        Keys.RemoveAll(k => k.Id == keyId);
        return Task.CompletedTask;
    }

    public Task SetChoiceAsync(Guid userId, AiTask task, string? modelId, CancellationToken cancellationToken = default)
    {
        Choices.RemoveAll(c => c.UserId == userId && c.Task == task);
        if (modelId is not null)
        {
            Choices.Add(new UserAiModelChoice { UserId = userId, Task = task, ModelId = modelId });
        }

        return Task.CompletedTask;
    }

    public UserAiKey Add(Guid userId, AiProvider provider, bool checkFailed = false, params UserAiCompatibleModel[] lines)
    {
        var key = new UserAiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            Label = provider.ToString(),
            ProtectedKey = "ciphertext",
            LastFour = "abcd",
            AddedAt = DateTimeOffset.UnixEpoch.AddMinutes(Keys.Count),
            UpdatedAt = DateTimeOffset.UnixEpoch,
            CheckFailed = checkFailed,
            CompatibleModels = [.. lines],
        };
        Keys.Add(key);
        return key;
    }
}
