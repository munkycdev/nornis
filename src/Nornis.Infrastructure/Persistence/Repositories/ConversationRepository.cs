using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IConversationRepository"/>
public class ConversationRepository : IConversationRepository
{
    private readonly NornisDbContext _context;

    public ConversationRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Conversation?> GetByAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var conversation = await _context.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.AdventureId == adventureId, cancellationToken);

        if (conversation is not null)
        {
            // Ordered here rather than in SQL so the SQLite test provider, which can't order a
            // DateTimeOffset, runs the same query production does. Should a question and its
            // answer ever share a tick, the GM's message comes first.
            conversation.Messages = conversation.Messages
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Role)
                .ToList();
        }

        return conversation;
    }

    public async Task CreateAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task AddMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        var conversation = await _context.LoadForUpdateAsync<Conversation>(message.ConversationId, cancellationToken);
        conversation.UpdatedAt = message.CreatedAt;
        _context.ConversationMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public Task<ConversationMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        _context.ConversationMessages
            .AsNoTracking()
            .Include(m => m.Conversation)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

    public async Task DeleteByAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        await _context.DeleteWhereAsync<ConversationMessage>(
            m => _context.Conversations.Any(c => c.Id == m.ConversationId && c.AdventureId == adventureId),
            cancellationToken);
        await _context.DeleteWhereAsync<Conversation>(c => c.AdventureId == adventureId, cancellationToken);
    }
}
