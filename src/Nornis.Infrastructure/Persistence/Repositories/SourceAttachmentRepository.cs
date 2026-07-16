using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class SourceAttachmentRepository : ISourceAttachmentRepository
{
    private readonly NornisDbContext _context;

    public SourceAttachmentRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<SourceAttachment> CreateAsync(SourceAttachment attachment, CancellationToken cancellationToken = default)
    {
        _context.SourceAttachments.Add(attachment);
        await _context.SaveChangesAsync(cancellationToken);
        return attachment;
    }

    public async Task<SourceAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.SourceAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<SourceAttachment>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        return await _context.SourceAttachments
            .AsNoTracking()
            .Where(a => a.SourceId == sourceId)
            .OrderBy(a => a.Ord)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<SourceAttachment> UpdateAsync(SourceAttachment attachment, CancellationToken cancellationToken = default)
    {
        _context.SourceAttachments.Update(attachment);
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(attachment).State = EntityState.Detached;
        return attachment;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Tracked-load delete: the InMemory provider used in tests lacks ExecuteDelete.
        var attachment = await _context.SourceAttachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment is not null)
        {
            _context.SourceAttachments.Remove(attachment);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
