using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class CheckInRepository : ICheckInRepository
{
    private readonly NornisDbContext _context;

    public CheckInRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<CheckIn> CreateAsync(CheckIn checkIn, CancellationToken cancellationToken = default)
    {
        _context.CheckIns.Add(checkIn);
        await _context.SaveChangesAsync(cancellationToken);
        return checkIn;
    }

    public async Task<CheckIn?> GetOpenByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.CheckIns
            .AsNoTracking()
            .Include(c => c.Questions.OrderBy(q => q.Order))
            .Where(c => c.WorldId == worldId && c.ClosedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CheckIn>> ListRecentByWorldAsync(Guid worldId, int maxCount, CancellationToken cancellationToken = default)
    {
        return await _context.CheckIns
            .AsNoTracking()
            .Include(c => c.Questions.OrderBy(q => q.Order))
            .Where(c => c.WorldId == worldId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<CheckInQuestion?> GetQuestionAsync(Guid questionId, CancellationToken cancellationToken = default)
    {
        return await _context.CheckInQuestions
            .AsNoTracking()
            .Include(q => q.CheckIn)
            .FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
    }

    public async Task UpdateQuestionAsync(CheckInQuestion question, CancellationToken cancellationToken = default)
    {
        var tracked = await _context.LoadForUpdateAsync<CheckInQuestion>(question.Id, cancellationToken);
        _context.Entry(tracked).CurrentValues.SetValues(question);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseAsync(Guid checkInId, DateTimeOffset closedAt, CancellationToken cancellationToken = default)
    {
        var tracked = await _context.LoadForUpdateAsync<CheckIn>(checkInId, cancellationToken);
        tracked.ClosedAt = closedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
