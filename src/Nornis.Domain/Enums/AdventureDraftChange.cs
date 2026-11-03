namespace Nornis.Domain.Enums;

/// <summary>What replaced the design draft, recorded on the revision that keeps the text before it (feature 37).</summary>
public enum AdventureDraftChange
{
    /// <summary>A suggested edit from the side conversation, applied.</summary>
    Edit,

    /// <summary>A conversation answer put into the draft.</summary>
    Put,

    /// <summary>The sections gathered back into the draft.</summary>
    Gather,

    /// <summary>The text of sections a split replaced, kept so it can be found again.</summary>
    Split,

    /// <summary>Markdown imported into the draft.</summary>
    Import,
}
