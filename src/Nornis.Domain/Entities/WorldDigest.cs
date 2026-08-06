namespace Nornis.Domain.Entities;

/// <summary>
/// The world's maintained synthesis — the wiki's index page. A generated read-model, not
/// an artifact: it must never flow through review or pollute the knowledge graph it
/// summarizes, so it hangs off the world as one replaceable row. Two renderings are one
/// generation act: the GM digest and the party recap are produced together from
/// separately-scoped passes, and regenerating replaces both — a row where one half is
/// newer than the other would misstate what the players' version knows.
/// </summary>
public class WorldDigest
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>The GM rendering: full record, hidden truths included. GMOnly surface.</summary>
    public string GmContentMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// The party rendering, generated from the Observer-floor view of the record —
    /// PartyVisible material only, no one's Private notes, no Hidden truth states —
    /// because it renders to every member of the world.
    /// </summary>
    public string PartyContentMarkdown { get; set; } = string.Empty;

    /// <summary>The model that generated this digest, snapshotted at generation time.</summary>
    public string Model { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; set; }

    public Guid GeneratedByUserId { get; set; }

    // Navigation property
    public World World { get; set; } = null!;
}
