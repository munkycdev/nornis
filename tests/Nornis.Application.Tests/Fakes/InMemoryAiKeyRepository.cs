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
