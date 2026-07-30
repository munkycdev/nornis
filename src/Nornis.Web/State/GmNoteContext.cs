namespace Nornis.Web.State;

/// <summary>
/// What the GM is currently looking at, so a note written from the global GM-note button can
/// name its subject without the GM retyping it.
/// </summary>
/// <remarks>
/// Extraction attaches a note to an artifact by matching names in the text, so the subject is
/// the difference between "he was lying about the caravan" landing on Captain Voss and it
/// landing nowhere. Pages with an obvious subject set it while they are on screen and clear it
/// on the way out; pages without one leave it null and the note stands on its own.
/// <para>
/// A setter owns the whole contract: release the subject before a load as well as on dispose,
/// and never claim it from a continuation that outlived the page. A stale subject is not a
/// cosmetic slip — it prefixes the wrong artifact's name onto the note and sends the GM's
/// correction to the wrong place. The dialog shows the subject and lets the GM drop it, which
/// is the last line of defence rather than the first.
/// </para>
/// </remarks>
public class GmNoteContext
{
    public string? Subject { get; private set; }

    public void Set(string? subject) =>
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();

    public void Clear() => Subject = null;
}
