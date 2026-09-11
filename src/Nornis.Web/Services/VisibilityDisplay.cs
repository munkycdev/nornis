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
}
