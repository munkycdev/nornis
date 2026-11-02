namespace Nornis.Web.Services;

/// <summary>
/// How Prep's section kinds look: their labels, icons and the order the Add section menu offers
/// them in. The names mirror <c>Nornis.Domain.Enums.AdventureSectionKind</c> across the deploy
/// boundary; <c>PrepDisplayTests</c> reads that file and fails when the two differ.
/// </summary>
public static class PrepDisplay
{
    public static readonly IReadOnlyList<(string Kind, string Label, string Icon)> Kinds =
    [
        ("Overview", "Overview", MudBlazor.Icons.Material.Outlined.Info),
        ("SessionClock", "Session clock", MudBlazor.Icons.Material.Outlined.Schedule),
        ("Chapter", "Chapter", MudBlazor.Icons.Material.Outlined.Bookmark),
        ("Scene", "Scene", MudBlazor.Icons.Material.Outlined.TheaterComedy),
        ("QuickReference", "Quick reference", MudBlazor.Icons.Material.Outlined.TableChart),
        ("Notes", "Notes", MudBlazor.Icons.Material.Outlined.StickyNote2),
    ];

    public static string Label(string kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind).Label ?? kind;

    public static string Icon(string kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind).Icon ?? MudBlazor.Icons.Material.Outlined.Article;

    /// <summary>
    /// The order after moving <paramref name="moved"/> into <paramref name="target"/>'s slot,
    /// sliding everything between them along: what a drop in the outline asks the API for.
    /// Unchanged when either id isn't in the list.
    /// </summary>
    public static IReadOnlyList<Guid> MoveTo(IReadOnlyList<Guid> order, Guid moved, Guid target)
    {
        var ids = order.ToList();
        var from = ids.IndexOf(moved);
        var to = ids.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return order;
        }

        ids.RemoveAt(from);
        ids.Insert(to, moved);
        return ids;
    }

    /// <summary>The order after moving one section up (-1) or down (+1); unchanged at either end.</summary>
    public static IReadOnlyList<Guid> MoveBy(IReadOnlyList<Guid> order, Guid moved, int delta)
    {
        var from = order.ToList().IndexOf(moved);
        var to = from + delta;
        return from < 0 || to < 0 || to >= order.Count ? order : MoveTo(order, moved, order[to]);
    }

    /// <summary>"4 of 7 sections written", the card's measure of how far along an Adventure is.</summary>
    public static string Progress(int filled, int total) =>
        total == 0 ? "No sections" : $"{filled} of {total} section{(total == 1 ? "" : "s")} written";
}
