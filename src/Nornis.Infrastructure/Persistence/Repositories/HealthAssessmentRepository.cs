using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class HealthAssessmentRepository : IHealthAssessmentRepository
{
    private readonly NornisDbContext _context;

    public HealthAssessmentRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<HealthAssessment> CreateAsync(
        HealthAssessment assessment,
        IReadOnlyList<ContinuityFinding> findings,
        CancellationToken cancellationToken = default)
    {
        _context.HealthAssessments.Add(assessment);
        if (findings.Count > 0)
        {
            _context.ContinuityFindings.AddRange(findings);
        }
        await _context.SaveChangesAsync(cancellationToken);
        return assessment;
    }

    public async Task<HealthAssessment?> GetLatestWithFindingsAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.HealthAssessments
            .AsNoTracking()
            .Where(a => a.WorldId == worldId)
            .OrderByDescending(a => a.CreatedAt)
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        var latest = await _context.HealthAssessments
            .AsNoTracking()
            .Where(a => a.WorldId == worldId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTimeOffset?)a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest;
    }

    public async Task<ContinuityFinding?> GetFindingByIdAsync(
        Guid findingId, CancellationToken cancellationToken = default)
    {
        // The parent assessment rides along so callers can scope the finding to a world
        // and date staleness against the assessment's CreatedAt.
        return await _context.ContinuityFindings
            .Include(f => f.HealthAssessment)
            .FirstOrDefaultAsync(f => f.Id == findingId, cancellationToken);
    }

    public async Task<ContinuityFinding> UpdateFindingAsync(
        ContinuityFinding finding, CancellationToken cancellationToken = default)
    {
        _context.ContinuityFindings.Update(finding);
        await _context.SaveChangesAsync(cancellationToken);
        return finding;
    }
}
