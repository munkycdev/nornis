using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// What an Adventure records about AI's part in it (feature 38). <see cref="WroteText"/> and
/// <see cref="MadeArt"/> are recorded, never chosen: only the repository's writes that put the
/// Loremaster's words or a generated cover into the Adventure set them, and nothing clears them,
/// not even Undo, because Undo restores text, not the fact that AI was used. The GM's own
/// <see cref="Declared"/> covers AI used outside Nornis.
///
/// Each Adventure owns its own instance, so never share one between two.
/// </summary>
public sealed class AdventureAiUse
{
    /// <summary>The Loremaster's text went into a section or the draft: a draft, Put in section or draft, or an applied edit.</summary>
    public bool WroteText { get; set; }

    /// <summary>A generated image became the cover. An uploaded cover never sets it.</summary>
    public bool MadeArt { get; set; }

    /// <summary>The GM's own tick. It can be unticked only while nothing is recorded.</summary>
    public bool Declared { get; set; }

    /// <summary>Recorded use: the box is ticked and can't be unticked. A method, so no mapping or serializer stores it.</summary>
    public bool IsLocked() => WroteText || MadeArt;

    /// <summary>What a print says: recorded use first, then the GM's declaration.</summary>
    public AiUseKind Kind() => (WroteText, MadeArt) switch
    {
        (true, true) => AiUseKind.TextAndArt,
        (true, false) => AiUseKind.Text,
        (false, true) => AiUseKind.Art,
        _ => Declared ? AiUseKind.Declared : AiUseKind.None,
    };
}
