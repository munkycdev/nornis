namespace Nornis.Domain.Models;

/// <summary>
/// The bibliographic details a GM gives an Adventure: what a library catalogues a book by
/// (feature 32). Only the four fields the GM edits live here. Type, publisher and author are
/// fixed, and are added when a print is resolved; see <see cref="PrintDetails.Resolve"/>.
///
/// Every field is optional. A null <see cref="GameSystem"/> means "the world's game system";
/// any other null means "not set". Construct through <see cref="Create"/>, which trims and turns
/// a blank string into null, so "cleared" and "never set" are one state.
/// </summary>
public sealed record AdventureDetails
{
    /// <summary>The same width as <c>World.GameSystem</c>.</summary>
    public const int MaxGameSystemLength = 200;

    public const int MaxEditionLength = 50;

    /// <summary>Free text: "1–3", "3rd", "any".</summary>
    public const int MaxLevelsLength = 30;

    public const int MinYear = 1900;

    /// <summary>How far past the current year a year may be: a book can be dated ahead of its release.</summary>
    public const int MaxYearsAhead = 5;

    /// <summary>The game system this adventure is written for. Null means the world's.</summary>
    public string? GameSystem { get; init; }

    public string? Edition { get; init; }

    public string? Levels { get; init; }

    public int? Year { get; init; }

    /// <summary>Normalises the strings (trimmed; blank is null). It does not check the bounds; see <see cref="Validate"/>.</summary>
    public static AdventureDetails Create(string? gameSystem, string? edition, string? levels, int? year) => new()
    {
        GameSystem = Normalize(gameSystem),
        Edition = Normalize(edition),
        Levels = Normalize(levels),
        Year = year,
    };

    /// <summary>The name of the first field outside its bound, or null when every field is within.</summary>
    public string? Validate(int currentYear)
    {
        if (GameSystem?.Length > MaxGameSystemLength)
        {
            return nameof(GameSystem);
        }

        if (Edition?.Length > MaxEditionLength)
        {
            return nameof(Edition);
        }

        if (Levels?.Length > MaxLevelsLength)
        {
            return nameof(Levels);
        }

        if (Year is { } year && (year < MinYear || year > currentYear + MaxYearsAhead))
        {
            return nameof(Year);
        }

        return null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
