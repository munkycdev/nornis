namespace Nornis.Application.Prep;

/// <summary>
/// The markup convention Prep writes in, and the one place it is defined. A plain blockquote
/// is read-aloud text. A blockquote whose first line is <see cref="PropMarker"/> is a prop the
/// GM hands over, and one whose first line is <see cref="AskMarker"/> is a question for the
/// players. A fenced block whose info string is <see cref="StatBlockInfo"/> is a stat block:
/// free "Label: value" lines, system-agnostic, stored and displayed, never computed.
///
/// The import parser, the print renderer and the drafting prompt read these. The Web's
/// editor toolbar can't reference Application, so it keeps a mirror that
/// <c>PrepMarkupMirrorTests</c> pins to this file.
/// </summary>
public static class PrepMarkup
{
    public const string PropMarker = "[!PROP]";

    public const string AskMarker = "[!ASK]";

    public const string StatBlockInfo = "statblock";
}
