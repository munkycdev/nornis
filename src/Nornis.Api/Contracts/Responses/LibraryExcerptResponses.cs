namespace Nornis.Api.Contracts.Responses;

public record LibraryExcerptCandidateResponse(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int Page,
    string Text);

/// <summary>The excerpt source just filed — enough to link to it and say where it stands.</summary>
public record LibraryExcerptFiledResponse(
    Guid SourceId,
    string Title,
    string ProcessingStatus,
    string? Slug = null);
