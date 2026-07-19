namespace Nornis.Domain.Enums;

public enum SourceType
{
    SessionNote,

    /// <summary>Legacy — retired from the capture UI; existing sources keep it.</summary>
    JournalEntry,

    /// <summary>Legacy — retired from the capture UI; existing sources keep it.</summary>
    Transcript,

    Upload,
    Image,
    HandwrittenNotes,
    WebLink,
    GMNote,
    ImportedNote,
    SessionAudio,
    FanFiction,

    /// <summary>A map image; extraction reads place names and positions into placemarks.</summary>
    Map,

    /// <summary>
    /// A synthetic, party-visible provenance record minted when a GM reveals GM-only
    /// knowledge to the party. Never captured by a user, never extracted — it exists so a
    /// revealed artifact/fact/relationship carries player-visible provenance ("learned via
    /// the reveal") instead of pointing at the GM's private source.
    /// </summary>
    Reveal
}
