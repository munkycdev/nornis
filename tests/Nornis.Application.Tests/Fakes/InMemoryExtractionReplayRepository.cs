using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryExtractionReplayRepository : IExtractionReplayRepository
{
    private readonly List<ExtractionReplay> _replays = [];

    public IReadOnlyList<ExtractionReplay> Replays => _replays.AsReadOnly();

    public void Seed(params ExtractionReplay[] replays) => _replays.AddRange(replays);

    public Task<ExtractionReplay?> CreateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default)
    {
        // Enforces the same filtered unique index the real schema does, so this fake cannot
        // disagree with production about whether a second Active replay is possible.
        if (replay.Status == ExtractionReplayStatus.Active
            && _replays.Any(r => r.WorldId == replay.WorldId && r.Status == ExtractionReplayStatus.Active))
        {
            return Task.FromResult<ExtractionReplay?>(null);
        }

        _replays.Add(replay);
        return Task.FromResult<ExtractionReplay?>(replay);
    }

    public Task<ExtractionReplay?> GetActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var replay = _replays.FirstOrDefault(
            r => r.WorldId == worldId && r.Status == ExtractionReplayStatus.Active);
        return Task.FromResult(replay);
    }

    public Task<ExtractionReplay> UpdateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default)
    {
        var index = _replays.FindIndex(r => r.Id == replay.Id);
        if (index >= 0)
        {
            _replays[index] = replay;
        }
        return Task.FromResult(replay);
    }
}
