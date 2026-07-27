namespace Nornis.Domain.Models;

/// <summary>
/// The one place that decides whether two artifact names are "the same name".
///
/// Deliberately conservative: trim, collapse internal whitespace runs to a single
/// space, compare ordinal-ignore-case. Nothing else. Articles are NOT stripped —
/// "Salt Factor" and "The Salt Factor" stay distinct, because deciding they are one
/// thing is a judgment call that belongs to the GM's manual merge, not to an
/// apply-time backstop that silently binds a new source's provenance to canon.
/// </summary>
public static class ArtifactNameKey
{
    /// <summary>
    /// Trim and collapse internal whitespace runs to a single space. Case is preserved,
    /// so this is also the form to compare on when a caller wants an exact-case tiebreak.
    /// </summary>
    public static string Collapse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var builder = new System.Text.StringBuilder(name.Length);
        var pendingSpace = false;

        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                // Only emit a separator once we know more content follows.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The comparison key: <see cref="Collapse"/> plus case folding. Suitable as a
    /// dictionary/grouping key when matching names in bulk.
    /// </summary>
    public static string Normalize(string? name) => Collapse(name).ToUpperInvariant();

    /// <summary>
    /// Whether two names are the same name under this policy. Empty/whitespace names
    /// never match anything, including each other — an unnamed artifact is not a
    /// duplicate of another unnamed one.
    /// </summary>
    public static bool AreEquivalent(string? a, string? b)
    {
        var left = Collapse(a);
        if (left.Length == 0)
            return false;

        var right = Collapse(b);
        if (right.Length == 0)
            return false;

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether two names match with identical casing (after trim/whitespace collapse).
    /// Used to break ties when canon already holds several same-name artifacts.
    /// </summary>
    public static bool AreExactCaseEquivalent(string? a, string? b)
    {
        var left = Collapse(a);
        if (left.Length == 0)
            return false;

        return string.Equals(left, Collapse(b), StringComparison.Ordinal);
    }
}
