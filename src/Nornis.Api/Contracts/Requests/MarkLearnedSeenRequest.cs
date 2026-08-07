namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// How far the reader has read. Sent explicitly rather than inferred from the read, so a reader
/// who opens the page and is interrupted does not lose the list.
/// </summary>
public record MarkLearnedSeenRequest(DateTimeOffset SeenThrough);
