using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// An import session with its backlog, ready for display. Every per-item state is derived
/// at read time from the item's source and its open proposal count — nothing here is
/// stored except <see cref="ImportItemInfo.Skipped"/>.
/// </summary>
/// <param name="CurrentItemId">The item the walk is on: the lowest-position item that is
/// neither done nor skipped. Null once the backlog is exhausted.</param>
/// <param name="CurrentIndex">1-based ordinal of the current item within
/// <paramref name="Items"/> — the "item n of N" in the progress header. Deliberately not
/// <see cref="ImportItemInfo.Position"/>, which is a raw sort key and may have gaps.</param>
/// <param name="SettledCount">Items already walked past: done or skipped.</param>
public record ImportSessionInfo(
    Guid Id,
    Guid WorldId,
    ImportSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ImportItemInfo> Items,
    Guid? CurrentItemId,
    int? CurrentIndex,
    int SettledCount);

public record ImportItemInfo(
    Guid Id,
    Guid SourceId,
    int Position,
    bool Skipped,
    string Title,
    DateTimeOffset? OccurredAt,
    SourceProcessingStatus ProcessingStatus,
    ImportItemState State,
    int OpenProposalCount);

public record AddImportNoteCommand(
    Guid WorldId,
    Guid SessionId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string Title,
    string? Body,
    DateTimeOffset? OccurredAt);
