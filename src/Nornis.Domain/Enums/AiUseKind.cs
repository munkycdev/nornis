namespace Nornis.Domain.Enums;

/// <summary>
/// What a print says about AI's part in an Adventure (feature 38). Recorded use (text, art)
/// outranks the GM's own declaration, which only matters when nothing is recorded.
/// </summary>
public enum AiUseKind
{
    /// <summary>Nothing recorded and nothing declared.</summary>
    None,

    /// <summary>The GM ticked the box for AI used outside Nornis; nothing is recorded.</summary>
    Declared,

    /// <summary>The Loremaster wrote text that went into the Adventure.</summary>
    Text,

    /// <summary>A generated image became the cover.</summary>
    Art,

    TextAndArt,
}
