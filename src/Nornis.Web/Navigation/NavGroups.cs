using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Nornis.Web.Navigation;

/// <summary>
/// Which live count an entry carries. The counts themselves come from the activity poll in
/// <c>NavMenu</c>; this only says which entry wears which one.
/// </summary>
public enum NavBadge
{
    None,
    PendingProposals,
    UnseenDisclosures,
    SourceActivity,
}

public sealed record NavItem(
    string Label,
    string Href,
    string Icon,
    NavBadge Badge = NavBadge.None,
    NavLinkMatch Match = NavLinkMatch.Prefix);

public sealed record NavGroup(string Label, IReadOnlyList<NavItem> Items, bool GmOnly = false);

/// <summary>An entry and the group it sits in.</summary>
public sealed record NavPlacement(NavGroup Group, NavItem Item);

/// <summary>
/// The sidebar, as data. One place says what is there, for whom, in what order; the menu
/// renders it in a loop and the reachability test reads it to know which routes have a door.
///
/// Grouped by what the member is doing rather than by feature: <b>Play</b> is the loop —
/// capture, review, learn; <b>World</b> is the record in its views; <b>Table</b> is the people
/// and the runs of play; <b>GM</b> is what only the GM does. Fourteen flat entries became four
/// groups of three to five on 2026-09-09 (feature 24, phase A).
/// </summary>
public static class NavGroups
{
    public static readonly IReadOnlyList<NavGroup> All =
    [
        new("Play",
        [
            new("Home", "/dashboard", Icons.Material.Outlined.Home, Match: NavLinkMatch.All),
            new("Capture", "/capture", Icons.Material.Outlined.EditNote),
            new("Review", "/review", Icons.Material.Outlined.FactCheck, NavBadge.PendingProposals),
            new("What you learned", "/learned", NornisIcons.NetworkIntelNode, NavBadge.UnseenDisclosures),
        ]),
        new("World",
        [
            new("Codex", "/artifacts", Icons.Material.Outlined.Hub),
            new("Storylines", "/storylines", Icons.Material.Outlined.AutoStories),
            new("Timeline", "/timeline", Icons.Material.Outlined.Route),
            new("Map", "/locations", Icons.Material.Outlined.Place),
            new("Library", "/library", Icons.Material.Outlined.MenuBook),
            new("Sources", "/sources", Icons.Material.Outlined.Layers, NavBadge.SourceActivity),
        ]),
        new("Table",
        [
            new("Campaigns", "/campaigns", Icons.Material.Outlined.Flag),
            new("Party", "/party", Icons.Material.Outlined.Groups),
            new("Members", "/members", Icons.Material.Outlined.People),
        ]),
        new("GM",
        [
            new("Reveal", "/convergence", Icons.Material.Outlined.Visibility),
            new("World memory", "/world-memory", Icons.Material.Outlined.Psychology),
            new("Settings", "/admin", Icons.Material.Outlined.Tune),
        ], GmOnly: true),
    ];

    /// <summary>
    /// The entry whose route the given path belongs to, if any — the default breadcrumb for a
    /// page that has not set its own. Longest matching href wins, so <c>/capture/ink</c> finds
    /// Capture and <c>/artifacts/{id}</c> finds Codex.
    /// </summary>
    public static NavItem? FindByPath(string path) => Locate(path)?.Item;

    /// <summary>
    /// The same lookup, with the group the entry sits in — for anything that says where a page
    /// is ("World › Map") rather than only what it is called. The tutorial reads its "where" lines
    /// from here so they follow the sidebar instead of describing a sidebar that has moved.
    /// </summary>
    public static NavPlacement? Locate(string path)
    {
        var normalized = "/" + path.Trim('/');
        return All
            .SelectMany(g => g.Items.Select(i => new NavPlacement(g, i)))
            .Where(p => normalized == p.Item.Href || normalized.StartsWith(p.Item.Href + "/", StringComparison.Ordinal))
            .OrderByDescending(p => p.Item.Href.Length)
            .FirstOrDefault();
    }
}
