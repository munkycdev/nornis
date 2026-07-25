namespace Nornis.Domain.Entities;

/// <summary>
/// One completed tutorial step for one user in one (demo) world. This is a cache of
/// detections, not the source of truth: state-backed steps are re-derivable from world
/// state, but caching keeps completion sticky if the underlying state later regresses
/// (e.g. the vetted proposal gets cleared) and avoids re-running detector queries.
/// Client-reported steps (page visits, view toggles) live only here.
/// </summary>
public class TutorialProgress
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>Step key from the fixed tutorial step list (feature 20 phase C).</summary>
    public string StepKey { get; set; } = string.Empty;

    public DateTimeOffset CompletedAt { get; set; }
}
