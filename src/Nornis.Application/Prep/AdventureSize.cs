using Nornis.Domain.Entities;

namespace Nornis.Application.Prep;

/// <summary>
/// How big an Adventure is, the one measure <see cref="Configuration.PrepOptions.MaxAdventureChars"/>
/// is checked against: every section's title and body. An export adds a heading's markup per
/// section and one filing line, which the two allowances below bound, so an Adventure within
/// the limit always composes to a body a source can hold.
/// </summary>
public static class AdventureSize
{
    /// <summary>The most markup an export adds around one section: "## ", and the blank lines either side.</summary>
    public const int ExportOverheadPerSection = 8;

    /// <summary>The longest filing line an export writes: its fixed words, a 200-character title and a date.</summary>
    public const int ExportFilingLineAllowance = 300;

    public static int Measure(IEnumerable<AdventureSection> sections) =>
        sections.Sum(s => s.Title.Length + s.BodyMarkdown.Length);
}
