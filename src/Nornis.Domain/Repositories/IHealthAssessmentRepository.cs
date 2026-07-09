using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IHealthAssessmentRepository
{
    /// <summary>Persists an assessment together with its findings in one unit.</summary>
    Task<HealthAssessment> CreateAsync(
        HealthAssessment assessment,
        IReadOnlyList<ContinuityFinding> findings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent assessment for the world with its findings attached,
    /// or null if the world has never been assessed.
    /// </summary>
    Task<HealthAssessment?> GetLatestWithFindingsAsync(
        Guid worldId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <see cref="HealthAssessment.CreatedAt"/> of the world's most recent
    /// assessment, or null if none exists. Used by the auto-trigger eligibility check.
    /// </summary>
    Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        Guid worldId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads a single finding by id (no assessment navigation), or null.</summary>
    Task<ContinuityFinding?> GetFindingByIdAsync(
        Guid findingId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a mutation to an existing finding (e.g. status transition).</summary>
    Task<ContinuityFinding> UpdateFindingAsync(
        ContinuityFinding finding,
        CancellationToken cancellationToken = default);
}
