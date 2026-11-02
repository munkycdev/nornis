namespace Nornis.Web.Services;

/// <summary>
/// Mirrors <c>Nornis.Application.Prep.PrepMarkup</c>, which defines Prep's markup convention.
/// The Web is a separate deployable with no reference to Application, so no compiler can keep
/// the two the same; <c>PrepMarkupMirrorTests</c> reads that file and fails when they differ.
/// </summary>
public static class PrepMarkup
{
    public const string PropMarker = "[!PROP]";

    public const string AskMarker = "[!ASK]";

    public const string StatBlockInfo = "statblock";

    /// <summary>
    /// What the editor's Prop button inserts: a blockquote whose first line is the marker, in
    /// the shape TipTap itself produces (one paragraph, lines split by &lt;br&gt;), so it saves
    /// as <c>&gt; [!PROP]</c> followed by the text.
    /// </summary>
    public static string PropBlockHtml() => MarkedBlockHtml(PropMarker, "Describe the prop the players can hold.");

    /// <summary>What the Ask button inserts: the same shape with the question marker.</summary>
    public static string AskBlockHtml() => MarkedBlockHtml(AskMarker, "What do you ask the players?");

    /// <summary>
    /// The stat block's starting lines. Labels and values, nothing computed and no game
    /// system assumed: the GM renames or adds labels as their system needs.
    /// </summary>
    public static string StatBlockTemplate { get; } = "Name: \nDefense: \nHealth: \nSpeed: \nAttacks: ";

    private static string MarkedBlockHtml(string marker, string prompt) =>
        $"<blockquote><p>{marker}<br>{prompt}</p></blockquote>";
}
