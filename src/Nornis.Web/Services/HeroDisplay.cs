using System.Globalization;
using Nornis.Web.ApiClient;
using Nornis.Web.Navigation;

namespace Nornis.Web.Services;

/// <summary>
/// The hero's picture as an address (feature 42). The API decides which picture a world shows;
/// this only spells where the browser fetches it, for Home, the world switcher and the public page.
/// </summary>
public static class HeroDisplay
{
    /// <summary>
    /// The picture's URL, or null when the hero has none. Key art goes through the Web's own route:
    /// the members' one, or the public one when <paramref name="publicSlug"/> is given.
    /// </summary>
    public static string? PictureUrl(WorldHeroDto hero, string? publicSlug = null) => hero.Picture switch
    {
        { Kind: "KeyArt", KeyArtStamp: { } stamp } => publicSlug is null
            ? Links.WorldArt(hero.WorldId, stamp)
            : Links.PublicWorldArt(publicSlug, stamp),
        { Kind: "Map", MapUrl: { Length: > 0 } url } => url,
        _ => null,
    };

    /// <summary>The line of facts under the world's name: system, sessions, entries, the last session.</summary>
    public static IReadOnlyList<string> Facts(WorldHeroDto hero)
    {
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(hero.GameSystem))
        {
            facts.Add(hero.GameSystem.Trim());
        }

        facts.Add(hero.SessionCount == 1 ? "1 session" : $"{hero.SessionCount} sessions");
        facts.Add(hero.EntryCount == 1 ? "1 codex entry" : $"{hero.EntryCount} codex entries");
        if (hero.LastSessionAt is { } last)
        {
            // A session's date is the day it was played, not a moment: written as a date.
            facts.Add($"last played {DateDisplay.Day(last)}");
        }

        return facts;
    }
}
