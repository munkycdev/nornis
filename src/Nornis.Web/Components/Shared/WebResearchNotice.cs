namespace Nornis.Web.Components.Shared;

/// <summary>
/// The reminder an Adventure that drew on web research gets before it leaves the GM's hands
/// (feature 40, Requirement 6.4): on the print page, and when a print of it is shared with the
/// party. It warns and never blocks.
/// </summary>
public static class WebResearchNotice
{
    public const string Warning =
        "This adventure used web research. If it draws on a published setting, check you have the right to share it.";

    /// <summary>The browser-local key that remembers the print page's reminder was seen for one Adventure: a reminder, not a record.</summary>
    public static string SeenKey(Guid adventureId) => $"nornis.webResearchNotice.{adventureId:N}";
}
