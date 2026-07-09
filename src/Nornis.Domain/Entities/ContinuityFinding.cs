using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One specific continuity risk the AI identified within a <see cref="HealthAssessment"/> —
/// a contradiction, dangling promise, dormant load-bearing storyline, timeline conflict, or a
/// summary that no longer matches its facts. Every finding is grounded in cited world item ids.
/// </summary>
public class ContinuityFinding
{
    public Guid Id { get; set; }

    public Guid HealthAssessmentId { get; set; }

    public ContinuityFindingCategory Category { get; set; }

    public ContinuityFindingSeverity Severity { get; set; }

    /// <summary>A short, single-sentence description of the problem.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>An optional concrete next step the GM could take to resolve the finding.</summary>
    public string? SuggestedAction { get; set; }

    /// <summary>
    /// JSON array of the resolved artifact/fact/relationship reference ids this finding cites,
    /// after server-side validation dropped any that didn't resolve to real world items.
    /// </summary>
    public string EvidenceJson { get; set; } = "[]";

    /// <summary>The primary artifact for UI navigation, when the finding centres on one.</summary>
    public Guid? ArtifactId { get; set; }

    public ContinuityFindingStatus Status { get; set; }

    // Navigation properties
    public HealthAssessment HealthAssessment { get; set; } = null!;
}
