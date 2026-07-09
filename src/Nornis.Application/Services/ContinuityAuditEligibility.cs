namespace Nornis.Application.Services;

/// <summary>
/// Pure, fully-derivable decision for whether a world is due an auto-triggered continuity
/// audit. Extracted from the hosted service so the rule can be unit-tested without any I/O.
/// </summary>
public static class ContinuityAuditEligibility
{
    /// <summary>
    /// A world is eligible when new canon was accepted since the last assessment, that
    /// acceptance has had time to settle (quiet period), and we have not assessed too recently.
    /// </summary>
    /// <param name="latestAcceptanceAt">Timestamp of the most recent accepted proposal, or null if none.</param>
    /// <param name="latestAssessmentAt">Timestamp of the most recent assessment, or null if never assessed.</param>
    /// <param name="now">The current time.</param>
    /// <param name="quietPeriod">Minimum age of the latest acceptance before a run is warranted.</param>
    /// <param name="minInterval">Minimum time since the last assessment before another run is allowed.</param>
    public static bool IsEligible(
        DateTimeOffset? latestAcceptanceAt,
        DateTimeOffset? latestAssessmentAt,
        DateTimeOffset now,
        TimeSpan quietPeriod,
        TimeSpan minInterval)
    {
        // No acceptance ever -> nothing to assess.
        if (latestAcceptanceAt is null)
        {
            return false;
        }

        // The acceptance must be newer than the last assessment (or there is no assessment yet).
        if (latestAssessmentAt is not null && latestAcceptanceAt.Value <= latestAssessmentAt.Value)
        {
            return false;
        }

        // Quiet period: let a burst of related acceptances settle before spending an AI call.
        if (now - latestAcceptanceAt.Value <= quietPeriod)
        {
            return false;
        }

        // At-most-daily: skip if an assessment ran within the minimum interval.
        if (latestAssessmentAt is not null && now - latestAssessmentAt.Value <= minInterval)
        {
            return false;
        }

        return true;
    }
}
