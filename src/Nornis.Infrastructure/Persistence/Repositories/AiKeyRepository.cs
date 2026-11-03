using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class AiKeyRepository : IAiKeyRepository
{
    private readonly NornisDbContext _context;

    public AiKeyRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserAiKey>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.UserAiKeys
            .AsNoTracking()
            .Include(k => k.CompatibleModels)
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.AddedAt)
            .ThenBy(k => k.Id)
            .ToListAsync(cancellationToken);

    public Task<UserAiKey?> GetAsync(Guid keyId, CancellationToken cancellationToken = default) =>
        _context.UserAiKeys
            .AsNoTracking()
            .Include(k => k.CompatibleModels)
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);

    public Task<UserAiModelChoice?> GetChoiceAsync(Guid userId, AiTask task, CancellationToken cancellationToken = default) =>
        _context.UserAiModelChoices
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Task == task, cancellationToken);

    public async Task<IReadOnlyList<UserAiModelChoice>> ListChoicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.UserAiModelChoices
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task MarkCheckFailedAsync(
        Guid keyId, DateTimeOffset keyVersion, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        _context.UserAiKeys
            .Where(k => k.Id == keyId && k.UpdatedAt == keyVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(k => k.CheckFailed, true)
                .SetProperty(k => k.CheckedAt, at), cancellationToken);

    public async Task SaveAsync(UserAiKey key, CancellationToken cancellationToken = default)
    {
        var stored = await _context.UserAiKeys
            .Include(k => k.CompatibleModels)
            .FirstOrDefaultAsync(k => k.Id == key.Id, cancellationToken);

        if (stored is null)
        {
            _context.UserAiKeys.Add(key);
            await _context.SaveChangesAsync(cancellationToken);
            Detach(key);
            return;
        }

        stored.Label = key.Label;
        stored.ProtectedKey = key.ProtectedKey;
        stored.LastFour = key.LastFour;
        stored.BaseUrl = key.BaseUrl;
        stored.UpdatedAt = key.UpdatedAt;
        stored.CheckedAt = key.CheckedAt;
        stored.CheckFailed = key.CheckFailed;

        // A line keeps its id while its task is listed, so a choice of "compatible:{id}" survives.
        foreach (var line in stored.CompatibleModels.Where(l => key.CompatibleModels.All(n => n.Task != l.Task)).ToList())
        {
            _context.UserAiCompatibleModels.Remove(line);
        }

        foreach (var line in key.CompatibleModels)
        {
            if (stored.CompatibleModels.FirstOrDefault(l => l.Task == line.Task) is { } kept)
            {
                kept.ModelName = line.ModelName;
                kept.SupportsImages = line.SupportsImages;
                kept.InputPerMillionUsd = line.InputPerMillionUsd;
                kept.OutputPerMillionUsd = line.OutputPerMillionUsd;
                line.Id = kept.Id;
            }
            else
            {
                line.UserAiKeyId = stored.Id;
                _context.UserAiCompatibleModels.Add(line);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        Detach(stored);
    }

    /// <summary>The repository's reads are untracked; its writes leave nothing tracked either, so a
    /// later write in the same request never meets a stale instance.</summary>
    private void Detach(UserAiKey key)
    {
        foreach (var line in key.CompatibleModels)
        {
            _context.Entry(line).State = EntityState.Detached;
        }

        _context.Entry(key).State = EntityState.Detached;
    }

    public async Task DeleteAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        var stored = await _context.UserAiKeys
            .Include(k => k.CompatibleModels)
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (stored is null)
        {
            return;
        }

        _context.UserAiKeys.Remove(stored);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetChoiceAsync(Guid userId, AiTask task, string? modelId, CancellationToken cancellationToken = default)
    {
        var stored = await _context.UserAiModelChoices
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Task == task, cancellationToken);

        if (modelId is null)
        {
            if (stored is null)
            {
                return;
            }

            _context.UserAiModelChoices.Remove(stored);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (stored is null)
        {
            stored = new UserAiModelChoice { UserId = userId, Task = task, ModelId = modelId };
            _context.UserAiModelChoices.Add(stored);
        }
        else
        {
            stored.ModelId = modelId;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(stored).State = EntityState.Detached;
    }
}
