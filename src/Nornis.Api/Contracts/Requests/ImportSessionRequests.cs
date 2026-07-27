namespace Nornis.Api.Contracts.Requests;

public record AddImportNoteRequest(string Title, string? Body = null, DateTimeOffset? OccurredAt = null);

/// <summary>Sources the world already holds, to be staged into the run in story order.</summary>
public record AddExistingSourcesRequest(IReadOnlyList<Guid> SourceIds);

/// <summary>The full ordered list of not-yet-started item ids — started notes keep their place.</summary>
public record ReorderImportItemsRequest(IReadOnlyList<Guid> ItemIds);

/// <summary>When <paramref name="SkipCurrent"/> is true the current note is passed over instead of
/// waited on. <paramref name="ExpectedItemId"/> is the item the client believes is current; when it
/// disagrees with the server the call is refused, so a stale screen can never skip an unseen note.</summary>
public record AdvanceImportSessionRequest(bool SkipCurrent = false, Guid? ExpectedItemId = null);
