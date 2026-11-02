namespace Nornis.Api.Contracts.Responses;

/// <summary>A passage to tick: an indexed chunk (<paramref name="ChunkId"/> set) or a Bibliotaph page, filed by <paramref name="Page"/>.</summary>
public record LibraryExcerptCandidateResponse(
    Guid? ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int Page,
    string Text,
    string? PageLabel = null);

/// <summary>One page of a Bibliotaph book. <paramref name="Page"/> is 1-based; <paramref name="Label"/> is the printed number.</summary>
public record LibraryPageResponse(int Page, string? Label, string Text);

/// <summary>The excerpt source just filed — enough to link to it and say where it stands.</summary>
public record LibraryExcerptFiledResponse(
    Guid SourceId,
    string Title,
    string ProcessingStatus,
    string? Slug = null);
