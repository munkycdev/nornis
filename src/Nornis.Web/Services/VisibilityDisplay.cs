namespace Nornis.Web.Services;

/// <summary>The visibility scopes a source can carry, in the order pickers offer them.</summary>
public static class VisibilityDisplay
{
    public static readonly string[] Scopes = ["PartyVisible", "GMOnly", "Private"];

    /// <summary>How a scope reads on screen: "GM only", not the identifier.</summary>
    public static string Label(string scope) => scope switch
    {
        "PartyVisible" => "Party visible",
        "GMOnly" => "GM only",
        "Private" => "Private",
        _ => scope,
    };

    /// <summary>
    /// Whether a list row should say its scope at all. Party visible is the resting state of a
    /// shared world, so a chip on every row saying so is noise; the exceptions are what a GM
    /// scans for.
    /// </summary>
    public static bool IsWorthShowing(string scope) => scope != "PartyVisible";
}
