using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

/// <summary>The convergence gauge with its ranking given: these candidates, in this order, or this error.</summary>
public sealed class StubConvergenceGauge : IConvergenceGaugeService
{
    public IReadOnlyList<ConvergenceCandidate> Candidates { get; set; } = [];

    public AppError? Error { get; set; }

    public Exception? Throw { get; set; }

    public List<WorldRole> Calls { get; } = [];

    public Task<AppResult<ConvergenceGauge>> GetGaugeAsync(Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        Calls.Add(role);
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(Error is not null
            ? AppResult<ConvergenceGauge>.Fail(Error)
            : AppResult<ConvergenceGauge>.Success(new ConvergenceGauge
            {
                WorldId = worldId,
                GeneratedAt = DateTimeOffset.UtcNow,
                AssessmentId = null,
                TotalCandidates = Candidates.Count,
                Candidates = Candidates,
            }));
    }

    public static ConvergenceCandidate Candidate(string anchorName, string description, int score) => new()
    {
        Kind = ConvergenceCandidateKind.Fact,
        Id = Guid.NewGuid(),
        AnchorArtifactId = Guid.NewGuid(),
        AnchorName = anchorName,
        Description = description,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        MissingArtifactIds = [],
        Components = ConvergenceScore.Components(30, 3, 0, null, null, contradictionAssessed: false),
        Score = score,
    };
}
