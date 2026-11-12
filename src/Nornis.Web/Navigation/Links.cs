namespace Nornis.Web.Navigation;

/// <summary>
/// The one place a detail-page URL is spelled. Every link, crumb, rail entry and redirect
/// builds through here, so the address of an artifact is decided once rather than at a
/// hundred call sites — which is how the GUID ended up in the address bar to begin with.
///
/// A page is addressed by its slug. The id is the fallback for a row the backfill has not
/// reached yet (a null slug means exactly that and nothing else), and the API accepts either,
/// so a link built either way opens the same page. The public world pages carry the world's
/// own slug in front.
/// </summary>
public static class Links
{
    public static string Artifact(string? slug, Guid id) => $"/codex/{Key(slug, id)}";

    /// <summary>World settings on its You tab: your display name, your characters, claiming a player.</summary>
    public const string WorldSettingsYou = "/settings?tab=you";

    /// <summary>Your account on its AI tab: keys and models.</summary>
    public const string ProfileAi = "/profile?tab=ai";

    /// <summary>Your account on its Account tab: sign-ins, Bibliotaph, appearance.</summary>
    public const string ProfileAccount = "/profile?tab=account";

    public static string Campaign(string? slug, Guid id) => $"/campaigns/{Key(slug, id)}";

    public static string Character(string? slug, Guid id) => $"/characters/{Key(slug, id)}";

    public static string Source(string? slug, Guid id) => $"/sources/{Key(slug, id)}";

    public static string LibraryDocument(string? slug, Guid id) => $"/library/{Key(slug, id)}";

    public static string Adventure(string? slug, Guid id) => $"/prep/{Key(slug, id)}";

    /// <summary>The booklet's PDF through the Web's own route (feature 34); <paramref name="download"/> saves it.</summary>
    public static string Booklet(Guid worldId, Guid adventureId, bool download = false) =>
        $"/prep-booklets/{worldId}/{adventureId}" + (download ? "?download=true" : string.Empty);

    /// <summary>
    /// An Adventure's chosen cover art through the Web's proxy (feature 38). The stamp is when the
    /// art was set, so a new cover is a new URL and the old one may be cached.
    /// </summary>
    public static string PrepCover(Guid worldId, Guid adventureId, DateTimeOffset stamp) =>
        $"/prep-covers/{worldId}/{adventureId}?v={stamp.ToUnixTimeMilliseconds()}";

    /// <summary>A world's key art through the Web's proxy (feature 42), stamped as a cover is.</summary>
    public static string WorldArt(Guid worldId, DateTimeOffset stamp) => $"/world-art/{worldId}?v={stamp.ToUnixTimeMilliseconds()}";

    /// <summary>A generated key-art option, for the GM's card.</summary>
    public static string WorldArtOption(Guid worldId, Guid optionId) => $"/world-art/{worldId}/options/{optionId}";

    /// <summary>A public world's key art, anonymously.</summary>
    public static string PublicWorldArt(string worldSlug, DateTimeOffset stamp) =>
        $"/w/{Uri.EscapeDataString(worldSlug)}/art?v={stamp.ToUnixTimeMilliseconds()}";

    /// <summary>The props sheet's PDF through the Web's own route (feature 38), always downloaded.</summary>
    public static string Props(Guid worldId, Guid adventureId) => $"/prep-props/{worldId}/{adventureId}";

    public static string Graph(string? slug, Guid id) => $"/graph/{Key(slug, id)}";

    public static string InkCapture(string? slug, Guid id) => $"/capture/ink/{Key(slug, id)}";

    public static string PublicArtifact(string worldSlug, string? slug, Guid id) => $"/w/{worldSlug}/codex/{Key(slug, id)}";

    public static string PublicCampaign(string worldSlug, string? slug, Guid id) => $"/w/{worldSlug}/campaigns/{Key(slug, id)}";

    public static string PublicSource(string worldSlug, string? slug, Guid id) => $"/w/{worldSlug}/sources/{Key(slug, id)}";

    /// <summary>The address segment for a row: its slug, or its id until it has one.</summary>
    public static string Key(string? slug, Guid id) => string.IsNullOrEmpty(slug) ? id.ToString() : slug;

    /// <summary>
    /// True when the page was opened by a key other than the slug it has now — an old id link,
    /// or a pasted slug in the wrong case — so the page can put the canonical address in the
    /// bar without a reload.
    /// </summary>
    public static bool IsCanonical(string key, string? slug) =>
        string.IsNullOrEmpty(slug) || string.Equals(key, slug, StringComparison.Ordinal);
}
