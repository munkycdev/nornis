namespace Nornis.Api.Contracts.Requests;

/// <param name="AsOf">When the sheet was current, which need not be when it was uploaded.</param>
public record AttachCharacterSnapshotRequest(Guid SourceId, DateTimeOffset AsOf, string? Note = null);
