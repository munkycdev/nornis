namespace Nornis.Domain.Entities;

/// <summary>
/// A point-in-time AI-assessed continuity health run for a campaign. Complements the fast/free
/// heuristic <c>CampaignHealth</c> score with an LLM read of the record for semantic continuity
/// problems, each captured as a <see cref="ContinuityFinding"/>.
/// </summary>
public class HealthAssessment
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The model string reported by the AI response that produced this assessment.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The blended score at assessment time: the heuristic score minus severity-weighted
    /// penalties for the findings that were Open when the assessment ran. A snapshot — the
    /// current effective score is recomputed from currently-Open findings.
    /// </summary>
    public int Score { get; set; }

    // Navigation properties
    public Campaign Campaign { get; set; } = null!;

    public ICollection<ContinuityFinding> Findings { get; set; } = [];
}
