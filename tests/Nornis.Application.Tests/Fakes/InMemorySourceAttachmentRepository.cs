using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemorySourceAttachmentRepository : ISourceAttachmentRepository
{
    private readonly List<SourceAttachment> _attachments = [];

    public IReadOnlyList<SourceAttachment> Attachments => _attachments.AsReadOnly();

    public void Seed(params SourceAttachment[] attachments) => _attachments.AddRange(attachments);

    public Task<SourceAttachment> CreateAsync(SourceAttachment attachment, CancellationToken cancellationToken = default)
    {
        _attachments.Add(attachment);
        return Task.FromResult(attachment);
    }

    public Task<SourceAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_attachments.FirstOrDefault(a => a.Id == id));
    }

    public Task<IReadOnlyList<SourceAttachment>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var list = _attachments
            .Where(a => a.SourceId == sourceId)
            .OrderBy(a => a.Ord)
            .ThenBy(a => a.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<SourceAttachment>>(list.AsReadOnly());
    }

    public Task<SourceAttachment> UpdateAsync(SourceAttachment attachment, CancellationToken cancellationToken = default)
    {
        var index = _attachments.FindIndex(a => a.Id == attachment.Id);
        if (index >= 0)
        {
            _attachments[index] = attachment;
        }
        return Task.FromResult(attachment);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _attachments.RemoveAll(a => a.Id == id);
        return Task.CompletedTask;
    }
}
