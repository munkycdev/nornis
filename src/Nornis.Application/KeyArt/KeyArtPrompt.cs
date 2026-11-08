using Nornis.Domain.Models;

namespace Nornis.Application.KeyArt;

/// <summary>
/// The key-art rules (feature 42). The brief is the GM's own words and goes to the image model
/// only: the Loremaster writes no key-art brief, so nothing GM-only can reach a picture the whole
/// party (and a public world's visitors) will see by way of it. Whatever the brief says, every
/// request carries the rules below.
/// </summary>
public static class KeyArtPrompt
{
    /// <summary>What every image request says after the GM's brief, so no brief can drop a rule.</summary>
    public const string ImageRules =
        "Painted landscape illustration, wide composition, for the banner of a tabletop role-playing world. "
        + "Keep the left third calm and uncluttered, since the world's name is set over it. "
        + "Absolutely no text, letters, numbers, logos, signatures or watermarks anywhere in the image.";

    /// <summary>The image request: the brief, then the rules.</summary>
    public static string ImagePrompt(string brief) => brief.Trim() + "\n\n" + ImageRules;

    /// <summary>Null when the brief can be used, otherwise why not.</summary>
    public static string? Invalid(string? brief) => brief switch
    {
        _ when string.IsNullOrWhiteSpace(brief) => "Describe the picture you want: a place, the light, the mood.",
        _ when brief.Trim().Length > WorldKeyArt.MaxBriefChars => $"A brief can be at most {WorldKeyArt.MaxBriefChars} characters.",
        _ => null,
    };
}
