using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryHealthAssessmentRepository : IHealthAssessmentRepository
{
    private readonly List<HealthAssessment> _assessments = [];
    private readonly List<ContinuityFinding> _findings = [];

    public IReadOnlyList<HealthAssessment> Assessments => _assessments.AsReadOnly();
    public IReadOnlyList<ContinuityFinding> Findings => _findings.AsReadOnly();

    public Task<HealthAssessment> CreateAsync(
        HealthAssessment assessment,
        IReadOnlyList<ContinuityFinding> findings,
        CancellationToken cancellationToken = default)
    {
        _assessments.Add(assessment);
        _findings.AddRange(findings);
        return Task.FromResult(assessment);
    }

    public Task<HealthAssessment?> GetLatestWithFindingsAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        var latest = _assessments
            .Where(a => a.WorldId == worldId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

        if (latest is not null)
        {
            latest.Findings = _findings.Where(f => f.HealthAssessmentId == latest.Id).ToList();
        }

        return Task.FromResult(latest);
    }

    public Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        var latest = _assessments
            .Where(a => a.WorldId == worldId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTimeOffset?)a.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(latest);
    }

    public Task<ContinuityFinding?> GetFindingByIdAsync(
        Guid findingId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_findings.FirstOrDefault(f => f.Id == findingId));
    }

    public Task<ContinuityFinding> UpdateFindingAsync(
        ContinuityFinding finding, CancellationToken cancellationToken = default)
    {
        var index = _findings.FindIndex(f => f.Id == finding.Id);
        if (index >= 0)
        {
            _findings[index] = finding;
        }
        return Task.FromResult(finding);
    }
}
