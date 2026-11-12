using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCheckInRepository : ICheckInRepository
{
    private readonly List<CheckIn> _checkIns = [];

    public IReadOnlyList<CheckIn> CheckIns => _checkIns.AsReadOnly();

    public void Seed(params CheckIn[] checkIns)
    {
        foreach (var checkIn in checkIns)
        {
            Attach(checkIn);
            _checkIns.Add(checkIn);
        }
    }

    public Task<CheckIn> CreateAsync(CheckIn checkIn, CancellationToken cancellationToken = default)
    {
        Attach(checkIn);
        _checkIns.Add(checkIn);
        return Task.FromResult(checkIn);
    }

    public Task<CheckIn?> GetOpenByWorldAsync(Guid worldId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_checkIns
            .Where(c => c.WorldId == worldId && c.ClosedAt is null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<CheckIn>> ListRecentByWorldAsync(Guid worldId, int maxCount, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CheckIn> result = _checkIns
            .Where(c => c.WorldId == worldId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(maxCount)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<CheckInQuestion?> GetQuestionAsync(Guid questionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_checkIns.SelectMany(c => c.Questions).FirstOrDefault(q => q.Id == questionId));

    public Task UpdateQuestionAsync(CheckInQuestion question, CancellationToken cancellationToken = default)
    {
        // The service mutates the tracked instance, so there is nothing to copy; a question
        // that was never written is the contract violation the real repository throws on.
        if (_checkIns.SelectMany(c => c.Questions).All(q => q.Id != question.Id))
        {
            throw new InvalidOperationException($"CheckInQuestion {question.Id} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid checkInId, DateTimeOffset closedAt, CancellationToken cancellationToken = default)
    {
        var checkIn = _checkIns.FirstOrDefault(c => c.Id == checkInId)
            ?? throw new InvalidOperationException($"CheckIn {checkInId} was not found.");
        checkIn.ClosedAt = closedAt;
        return Task.CompletedTask;
    }

    private static void Attach(CheckIn checkIn)
    {
        foreach (var question in checkIn.Questions)
        {
            question.CheckIn = checkIn;
            if (question.CheckInId == Guid.Empty)
            {
                question.CheckInId = checkIn.Id;
            }
        }
    }
}
