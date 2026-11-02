using System.Text.Json.Serialization;

namespace Nornis.Application.Bibliotaph;

// The shapes of Bibliotaph's /api/nornis/v1 endpoints, named as the Bibliotaph integration spec
// names them. That spec is the contract: a field changes there first, then here. Page indexes
// are 0-based at this boundary; BibliotaphPages converts them.

/// <summary>One event in a webhook batch. <see cref="Kind"/> is a wire name from <see cref="BibliotaphWireNames"/>.</summary>
public sealed record BibliotaphWorldEventMessage(
    Guid EventId,
    string Kind,
    Guid WorldId,
    string WorldName,
    DateTimeOffset OccurredAt);

/// <summary>A book in a world's collection.</summary>
public sealed record BibliotaphBook(
    string EntryId,
    string DocumentId,
    string Title,
    string? Subtitle,
    string? Type,
    string? System,
    int? PageCount,
    string? Format,
    string? CoverName,
    Guid? NornisFileId);

public sealed record BibliotaphBookList(IReadOnlyList<BibliotaphBook> Books);

public sealed record BibliotaphPassagesRequest(
    string Question,
    int Limit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? EntryIds,
    string Mode);

public sealed record BibliotaphPassage(
    string EntryId,
    string DocumentId,
    string Title,
    int PageIndex,
    string? PageLabel,
    string Text,
    double Score);

public sealed record BibliotaphPassages(
    bool Covered,
    bool MeaningUsed,
    IReadOnlyList<BibliotaphPassage> Pages);

public sealed record BibliotaphPageText(int PageIndex, string? PageLabel, string Text);

public sealed record BibliotaphPageTexts(IReadOnlyList<BibliotaphPageText> Pages);

public sealed record BibliotaphTicketRequest(
    string DocumentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? StartPage,
    string ViewerName,
    string Renew);

public sealed record BibliotaphTicket(string Url, DateTimeOffset ExpiresAt);

public sealed record BibliotaphCover(byte[] Bytes, string ContentType);

/// <summary>The contract's passage modes.</summary>
public static class BibliotaphPassageModes
{
    /// <summary>The Loremaster: a weak match comes back as not covered, with no pages.</summary>
    public const string Ask = "ask";

    /// <summary>A GM choosing excerpts: the closest pages come back anyway.</summary>
    public const string Search = "search";
}
