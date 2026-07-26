using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A GM's standing decision that a continuity issue is not a problem, recorded per world
/// rather than per assessment run.
/// <para>
/// Each audit run produces a brand-new <see cref="HealthAssessment"/> with brand-new
/// <see cref="ContinuityFinding"/> rows, so a dismissal attached only to a finding dies with
/// that run's snapshot. This registry is the durable record: it is consulted on every run,
/// forever, so a re-detected issue arrives already dismissed and never re-penalizes the score.
/// </para>
/// </summary>
public class ContinuityDismissal
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>The category of the finding that was dismissed.</summary>
    public ContinuityFindingCategory Category { get; set; }

    /// <summary>
    /// The dismissed finding's evidence — a JSON array of "artifact:GUID" / "fact:GUID" /
    /// "rel:GUID" refs, copied verbatim at dismissal time. A later finding matches this
    /// registry row when the category is the same and the two evidence sets intersect.
    /// </summary>
    public string EvidenceJson { get; set; } = "[]";

    public DateTimeOffset DismissedAtUtc { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;
}
