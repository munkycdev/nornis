using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nornis.Domain.Models;

/// <summary>
/// The one definition of what a URL slug is in Nornis: lowercase a-z and 0-9 joined by single
/// hyphens, at most <see cref="MaxLength"/> characters. The world's GM-typed public slug is
/// validated against <see cref="Pattern"/>; the per-entity slugs that name detail pages
/// (<c>/artifacts/the-ashen-king</c>) are derived from a name by <see cref="From"/>. Both
/// alphabets have to agree, because a slug that one route accepts and another rejects is a
/// link that works from one page and not from the next.
///
/// Derivation is deliberately lossy and boring: accents are stripped rather than transliterated,
/// anything that is not a letter or digit becomes a separator, and a name with nothing usable in
/// it falls back to the word the caller supplies. Uniqueness within a world is not this type's
/// concern — the persistence layer adds a numeric suffix when two names collide, see
/// <c>SlugAssigner</c>.
/// </summary>
public static partial class Slug
{
    public const int MaxLength = 60;

    /// <summary>
    /// Room for the collision suffix (<c>-2</c> … <c>-9999</c>) beyond <see cref="MaxLength"/>:
    /// the column width, and the longest value a resolver should bother matching.
    /// </summary>
    public const int MaxStoredLength = MaxLength + 5;

    /// <summary>The GM-typed form: three to sixty characters, hyphens only in the interior. A derived
    /// slug shares the alphabet but may be shorter, since a two-letter name is still a name.</summary>
    public const string Pattern = "^[a-z0-9][a-z0-9-]{1,58}[a-z0-9]$";

    [GeneratedRegex(Pattern)]
    private static partial Regex ValidSlug();

    [GeneratedRegex("-{2,}")]
    private static partial Regex RepeatedHyphens();

    public static bool IsValid(string? slug) => slug is not null && ValidSlug().IsMatch(slug);

    /// <summary>
    /// Derives a slug from a display name. Never returns empty: when nothing in the name survives
    /// (all punctuation, or a script outside Latin letters), the result is <paramref name="fallback"/>
    /// run through the same rule, so the caller passes a plain word such as <c>"artifact"</c>.
    /// </summary>
    public static string From(string? name, string fallback)
    {
        var derived = Derive(name);
        if (derived.Length > 0)
        {
            return derived;
        }

        var fallen = Derive(fallback);
        if (fallen.Length == 0)
        {
            throw new ArgumentException("The fallback must itself yield a non-empty slug.", nameof(fallback));
        }

        return fallen;
    }

    private static string Derive(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            // Accents fall away; so do apostrophes, since "players-handbook" reads as the title
            // and "player-s-handbook" does not.
            if (category == UnicodeCategory.NonSpacingMark || ch is '\'' or '’')
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                builder.Append('-');
            }
        }

        var collapsed = RepeatedHyphens().Replace(builder.ToString(), "-").Trim('-');
        if (collapsed.Length > MaxLength)
        {
            collapsed = collapsed[..MaxLength].TrimEnd('-');
        }

        return collapsed;
    }
}
