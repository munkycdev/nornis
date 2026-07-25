namespace Nornis.Application.Configuration;

public class DemoWorldOptions
{
    public const string SectionName = "DemoWorlds";

    /// <summary>
    /// Kill switch for public views of demo worlds. When false, demo worlds cannot enable
    /// public access and existing public views of demo worlds stop being served (slugs are
    /// kept, so flipping back restores the same links).
    /// </summary>
    public bool PublicAccessEnabled { get; set; } = true;

    /// <summary>
    /// Path to the demo-world template package (a world-export zip). Relative paths resolve
    /// against the app's content root. Empty/missing disables demo world creation.
    /// </summary>
    public string? TemplatePath { get; set; }

    /// <summary>Demo world creations allowed per user per rolling day.</summary>
    public int MaxCreationsPerUserPerDay { get; set; } = 1;
}
