namespace Nornis.Api.Contracts.Requests;

/// <summary>Edits applied atomically with the reprocess; null fields keep current values.</summary>
public record ReprocessSourceRequest(
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    bool ClearOccurredAt = false);
