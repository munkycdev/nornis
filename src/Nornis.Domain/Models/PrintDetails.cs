namespace Nornis.Domain.Models;

/// <summary>
/// An Adventure's details as a print carries them: complete, with every default applied and
/// the fixed values filled in (feature 32). The Details card shows one as a preview, the
/// booklet prints one, and a Library document filed from a print keeps one as a snapshot that
/// never changes afterwards.
///
/// <see cref="Resolve"/> is the only place a default or a fixed value is decided. Type and
/// publisher are stored on each print even though they are constants today, so a booklet
/// already filed keeps its wording if the constants ever change.
/// </summary>
public sealed record PrintDetails
{
    /// <summary>Every Prep adventure's type (Dave, 2026-11-03).</summary>
    public const string NornisType = "Nornis adventure";

    /// <summary>Every Prep adventure's publisher (Dave, 2026-11-03).</summary>
    public const string NornisPublisher = "Nornis Press";

    /// <summary>The same width as <c>Adventure.Title</c>.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The same width as <c>User.Username</c>.</summary>
    public const int MaxAuthorLength = 200;

    /// <summary>The longest <see cref="Type"/> or <see cref="Publisher"/> a print can store.</summary>
    public const int MaxFixedLength = 50;

    /// <summary>The Adventure's title when the print was made.</summary>
    public required string Title { get; init; }

    /// <summary>The Adventure's game system, or the world's. Null means neither had one.</summary>
    public string? GameSystem { get; init; }

    public string? Edition { get; init; }

    public required string Type { get; init; }

    public string? Levels { get; init; }

    public required string Publisher { get; init; }

    /// <summary>The username of the Adventure's creator when the print was made.</summary>
    public required string Author { get; init; }

    public int? Year { get; init; }

    /// <summary>
    /// The details a print of this Adventure carries now. A null game system falls back to the
    /// world's; type and publisher are the fixed values; the author is the creator's username.
    /// </summary>
    public static PrintDetails Resolve(string title, AdventureDetails details, string? worldGameSystem, string authorUsername) => new()
    {
        Title = title,
        GameSystem = details.GameSystem ?? (string.IsNullOrWhiteSpace(worldGameSystem) ? null : worldGameSystem.Trim()),
        Edition = details.Edition,
        Type = NornisType,
        Levels = details.Levels,
        Publisher = NornisPublisher,
        Author = authorUsername,
        Year = details.Year,
    };
}
