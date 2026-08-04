namespace Nornis.Application.Configuration;

/// <summary>Settings for the abandoned-upload sweep. See <see cref="Services.PendingUploadSweeper"/>.</summary>
public class UploadSweepOptions
{
    public const string SectionName = "UploadSweep";

    /// <summary>
    /// How long a PendingUpload row waits before it counts as abandoned. An upload still in
    /// flight looks exactly like one that was given up on, so only age separates them — this has
    /// to comfortably exceed both the write SAS's lifetime and the slowest plausible upload of a
    /// file at the size cap, or the sweep deletes work in progress.
    /// </summary>
    public int AbandonedAfterHours { get; set; } = 24;

    /// <summary>How often the sweep runs. Non-positive disables it.</summary>
    public double TickIntervalHours { get; set; } = 6;

    /// <summary>
    /// Rows removed per kind per sweep. A bound rather than a target: it keeps one tick from
    /// turning into a long storage-delete loop, and the remainder is picked up next tick because
    /// the query is oldest-first.
    /// </summary>
    public int MaxPerSweep { get; set; } = 200;
}
