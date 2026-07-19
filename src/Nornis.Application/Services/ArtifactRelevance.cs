namespace Nornis.Application.Services;

/// <summary>
/// Relevance scoring for global artifact search.
///
/// Tiers are exclusive — an artifact scores the single best tier it qualifies for, never a
/// sum — so the resulting order is explainable to a user: a name match always outranks a
/// summary match, and a whole-word hit always outranks a mid-word substring. Multi-word
/// queries fall back to an all-tokens-present tier so "voss captain" still finds
/// "Captain Voss" without promoting it above a real phrase match.
/// </summary>
public static class ArtifactRelevance
{
    public const int ExactName = 100;
    public const int NamePrefix = 80;
    public const int NameWord = 60;
    public const int NameContains = 40;
    public const int NameAllTokens = 30;
    public const int SummaryWord = 20;
    public const int SummaryContains = 10;
    public const int SummaryAllTokens = 5;
    public const int NoMatch = 0;

    /// <summary>
    /// Scores one artifact's searchable text against a query. Returns <see cref="NoMatch"/>
    /// when the term appears nowhere, which callers use to drop the row entirely.
    /// </summary>
    public static int Score(string? name, string? summary, string? term)
    {
        var needle = term?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return NoMatch;
        }

        var haystackName = name ?? string.Empty;

        if (string.Equals(haystackName, needle, StringComparison.OrdinalIgnoreCase))
        {
            return ExactName;
        }

        if (haystackName.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            return NamePrefix;
        }

        if (ContainsWholeWord(haystackName, needle))
        {
            return NameWord;
        }

        if (haystackName.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return NameContains;
        }

        // Only worth checking for genuinely multi-word queries; for a single token this
        // tier can never match anything the substring tiers above did not already catch.
        var tokens = Tokenize(needle);
        var multiToken = tokens.Length > 1;

        if (multiToken && tokens.All(t => haystackName.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            return NameAllTokens;
        }

        if (string.IsNullOrEmpty(summary))
        {
            return NoMatch;
        }

        if (ContainsWholeWord(summary, needle))
        {
            return SummaryWord;
        }

        if (summary.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return SummaryContains;
        }

        if (multiToken && tokens.All(t => summary.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            return SummaryAllTokens;
        }

        return NoMatch;
    }

    private static string[] Tokenize(string term) =>
        term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// True when <paramref name="word"/> appears in <paramref name="text"/> bounded by
    /// non-alphanumerics on both sides — so "Voss" matches "Captain Voss" but not "Vossberg".
    /// </summary>
    private static bool ContainsWholeWord(string text, string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return false;
        }

        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);

            if (before && after)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
