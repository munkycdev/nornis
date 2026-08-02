namespace Nornis.Domain.Enums;

public enum ExtractionReplayStatus
{
    /// <summary>Walking the timeline: one source is queued, processing, or awaiting review.</summary>
    Active,

    /// <summary>Every eligible source after the starting point has been re-extracted and reviewed.</summary>
    Completed,

    /// <summary>Stopped by the GM. The in-flight source finishes its normal lifecycle; no further advance.</summary>
    Canceled
}
