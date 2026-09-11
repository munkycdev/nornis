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

    public static string Campaign(string? slug, Guid id) => $"/campaigns/{Key(slug, id)}";

    public static string Character(string? slug, Guid id) => $"/characters/{Key(slug, id)}";

    public static string Source(string? slug, Guid id) => $"/sources/{Key(slug, id)}";

    public static string LibraryDocument(string? slug, Guid id) => $"/library/{Key(slug, id)}";

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
