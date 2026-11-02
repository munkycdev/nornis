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
}
