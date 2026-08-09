namespace Nornis.Web;

/// <summary>
/// Icons Nornis needs that MudBlazor's bundled Material set does not carry. Each is the inner
/// markup of an SVG, which is what <c>MudIcon</c> expects when an icon is not a font ligature.
/// </summary>
public static class NornisIcons
{
    /// <summary>
    /// Google's Material Symbols <c>network_intel_node</c> (Apache 2.0). MudBlazor 9.7 ships the
    /// older Material Icons set, which stops short of it.
    ///
    /// <para>Wrapped in a nested <c>svg</c> because Material Symbols are drawn in a 960-unit box
    /// with the origin at the baseline, and <c>MudIcon</c> renders custom markup inside its own
    /// <c>0 0 24 24</c> viewBox with no way to change it from a nav link. The inner viewBox does
    /// the scaling, so the path stays byte-identical to the one Google publishes.</para>
    /// </summary>
    public const string NetworkIntelNode =
        """
        <svg viewBox="0 -960 960 960"><path d="M323-160q-11 0-20.5-5.5T288-181l-78-139h58l40 80h92v-40h-68l-40-80H188l-57-100q-2-5-3.5-10t-1.5-10q0-4 5-20l57-100h104l40-80h68v-40h-92l-40 80h-58l78-139q5-10 14.5-15.5T323-800h97q17 0 28.5 11.5T460-760v160h-60l-40 40h100v120h-88l-40-80h-92l-40 40h108l40 80h112v200q0 17-11.5 28.5T420-160h-97Zm180.5-23.5Q480-207 480-240q0-23 11-40.5t29-28.5v-342q-18-11-29-28.5T480-720q0-33 23.5-56.5T560-800q33 0 56.5 23.5T640-720q0 23-11 40.5T600-651v101l80-48q0-34 23.5-58t56.5-24q33 0 56.5 23.5T840-600q0 33-23.5 56.5T760-520q-11 0-20.5-2.5T721-530l-91 55 101 80q7-3 14-4t15-1q33 0 56.5 23.5T840-320q0 33-23.5 56.5T760-240q-37 0-60.5-28T681-332l-81-65v89q18 11 28.5 28.5T639-240q0 33-23 56.5T560-160q-33 0-56.5-23.5Z"/></svg>
        """;
}
