using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One row of the outbox that keeps Bibliotaph's world collections current (feature 31). It is
/// written in the same save as the change it reports, and the Worker delivers it to Bibliotaph's
/// webhook, retrying with backoff. Bibliotaph applies events idempotently by <see cref="Id"/>,
/// so delivering one twice is harmless.
///
/// <see cref="WorldId"/> deliberately has no foreign key: a world's deletion must not delete its
/// own <c>deleted</c> event.
/// </summary>
public class BibliotaphWorldEvent
{
    public const int MaxWorldNameLength = 200;

    /// <summary>The event id sent to Bibliotaph.</summary>
    public Guid Id { get; set; }

    /// <summary>The world's creator, whose connection delivers the event.</summary>
    public Guid UserId { get; set; }

    public Guid WorldId { get; set; }

    public BibliotaphWorldEventKind Kind { get; set; }

    /// <summary>The world's name when the event happened, cut to <see cref="MaxWorldNameLength"/>.</summary>
    public string WorldName { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>The last failure as a code and status, never a response body. Null before any failure.</summary>
    public string? LastError { get; set; }

    /// <summary>Null until Bibliotaph accepted it.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Set when delivery was given up, seven days after it happened.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    public User User { get; set; } = null!;
}
