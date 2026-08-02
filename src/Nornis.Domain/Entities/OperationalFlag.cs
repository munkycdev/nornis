namespace Nornis.Domain.Entities;

/// <summary>
/// An operator switch that takes effect without a redeploy.
///
/// Per-world budgets cap spend over a day; they cannot stop it now. During a provider
/// incident, a runaway loop, or a bill that is climbing faster than anyone expected, the
/// only lever before this was a code change and a rollout — which is minutes at best and
/// needs a working pipeline, exactly when things are already going wrong.
///
/// Keyed by name rather than by a Guid, for the same reason <see cref="WorkerHeartbeat"/>
/// is: the row *is* the flag, and a second row for the same flag would be a bug rather
/// than a record.
/// </summary>
public class OperationalFlag
{
    /// <summary>Pauses every paid AI call across the system. See <see cref="OperationalFlagNames"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>
    /// Shown to users when an interactive path refuses, so a pause reads as deliberate rather
    /// than broken. Free text: an operator writes it at the moment they flip the switch.
    /// </summary>
    public string? Reason { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The flags the system knows about. Adding one means adding a reader for it.</summary>
public static class OperationalFlagNames
{
    public const string AiPaused = "ai-paused";
}
