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

/// <summary>Which draft changes put the Loremaster's words into the draft (feature 38's AI use).</summary>
public static class AdventureDraftChangeExtensions
{
    /// <summary>
    /// True for an applied edit and Put in draft; false for the changes that only move the GM's
    /// own text (gather, split, import). A new kind of change must be classified here, or it throws.
    /// </summary>
    public static bool IsLoremasterWriting(this AdventureDraftChange change) => change switch
    {
        AdventureDraftChange.Edit or AdventureDraftChange.Put => true,
        AdventureDraftChange.Gather or AdventureDraftChange.Split or AdventureDraftChange.Import => false,
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "An unclassified draft change."),
    };
}
