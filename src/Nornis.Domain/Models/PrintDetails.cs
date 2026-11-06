using Nornis.Domain.Enums;

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
    /// What the print says about AI's part in the Adventure (feature 38). Null only on prints
    /// filed before Nornis 3.9, which recorded nothing: unknown, not "none".
    /// </summary>
    public AiUseKind? AiUse { get; init; }

    /// <summary>
    /// The Adventure drew on web research when the print was made (feature 40), so its rights page
    /// says so. Null only on prints filed before Nornis 3.11, read as false.
    /// </summary>
    public bool? ResearchedOnWeb { get; init; }

    /// <summary>The game system and edition as one line ("Shadowdark 2nd"), or null when neither is set. A method, so no serializer or mapping stores it.</summary>
    public string? SystemLine() => JoinSystem(GameSystem, Edition);

    /// <summary>
    /// The details a print of this Adventure carries now. A null game system falls back to the
    /// world's; type and publisher are the fixed values; the author is the creator's username.
    /// <paramref name="researchedOnWeb"/> is the Adventure's record that it drew on the web (feature 40).
    /// </summary>
    public static PrintDetails Resolve(
        string title, AdventureDetails details, string? worldGameSystem, string authorUsername, AiUseKind aiUse,
        bool researchedOnWeb = false) => new()
        {
            Title = title,
            GameSystem = GameSystemOf(details, worldGameSystem),
            Edition = details.Edition,
            Type = NornisType,
            Levels = details.Levels,
            Publisher = NornisPublisher,
            Author = authorUsername,
            Year = details.Year,
            AiUse = aiUse,
            ResearchedOnWeb = researchedOnWeb,
        };

    /// <summary>
    /// The game system an Adventure is written for: its own, or the world's (feature 32). The
    /// print and Prep's AI prompts (feature 35) both read it here, so the AI writes for the
    /// system the booklet will name.
    /// </summary>
    public static string? GameSystemOf(AdventureDetails details, string? worldGameSystem) =>
        details.GameSystem ?? (string.IsNullOrWhiteSpace(worldGameSystem) ? null : worldGameSystem.Trim());

    /// <summary>The system line for an Adventure's details: its game system (or the world's) and its edition.</summary>
    public static string? SystemOf(AdventureDetails details, string? worldGameSystem) =>
        JoinSystem(GameSystemOf(details, worldGameSystem), details.Edition);

    private static string? JoinSystem(string? gameSystem, string? edition)
    {
        var line = string.Join(' ', new[] { gameSystem, edition }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
        return line.Length > 0 ? line : null;
    }
}
