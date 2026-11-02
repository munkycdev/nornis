using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// The side conversation's repository in memory, with the real one's contract: reads hand back
/// copies (the real reads are untracked) with messages oldest first, and one conversation per
/// Adventure.
/// </summary>
public class InMemoryConversationRepository : IConversationRepository
{
    private readonly List<Conversation> _conversations = [];
    private readonly List<ConversationMessage> _messages = [];

    public IReadOnlyList<Conversation> Conversations => _conversations;

    public IReadOnlyList<ConversationMessage> Messages => _messages;

    public Task<Conversation?> GetByAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var stored = _conversations.FirstOrDefault(c => c.AdventureId == adventureId);
        return Task.FromResult(stored is null ? null : CopyOf(stored));
    }

    public Task CreateAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        if (_conversations.Any(c => c.AdventureId == conversation.AdventureId))
        {
            throw new InvalidOperationException("An Adventure holds at most one conversation.");
        }

        _conversations.Add(new Conversation
        {
            Id = conversation.Id,
            WorldId = conversation.WorldId,
            UserId = conversation.UserId,
            AdventureId = conversation.AdventureId,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
        });
        _messages.AddRange(conversation.Messages.Select(CopyOf));
        return Task.CompletedTask;
    }

    public Task AddMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        var conversation = _conversations.Single(c => c.Id == message.ConversationId);
        conversation.UpdatedAt = message.CreatedAt;
        _messages.Add(CopyOf(message));
        return Task.CompletedTask;
    }

    public Task<ConversationMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var stored = _messages.FirstOrDefault(m => m.Id == messageId);
        if (stored is null)
        {
            return Task.FromResult<ConversationMessage?>(null);
        }

        var copy = CopyOf(stored);
        copy.Conversation = CopyOf(_conversations.Single(c => c.Id == stored.ConversationId));
        return Task.FromResult<ConversationMessage?>(copy);
    }

    public Task DeleteByAdventureAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        var ids = _conversations.Where(c => c.AdventureId == adventureId).Select(c => c.Id).ToHashSet();
        _messages.RemoveAll(m => ids.Contains(m.ConversationId));
        _conversations.RemoveAll(c => ids.Contains(c.Id));
        return Task.CompletedTask;
    }

    private Conversation CopyOf(Conversation c) => new()
    {
        Id = c.Id,
        WorldId = c.WorldId,
        UserId = c.UserId,
        AdventureId = c.AdventureId,
        Title = c.Title,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
        Messages = _messages.Where(m => m.ConversationId == c.Id).OrderBy(m => m.CreatedAt).ThenBy(m => m.Role).Select(CopyOf).ToList(),
    };

    private static ConversationMessage CopyOf(ConversationMessage m) => new()
    {
        Id = m.Id,
        ConversationId = m.ConversationId,
        Role = m.Role,
        Content = m.Content,
        Model = m.Model,
        CreatedAt = m.CreatedAt,
    };
}
