using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Tests.Fakes;

public class FakeProposalApplicator : IProposalApplicator
{
    private AppResult<ApplyResult>? _nextResult;

    /// <summary>The filter the last ApplyAsync call was made with — lets callers assert
    /// that name resolution runs through the accepting reviewer's eyes.</summary>
    public VisibilityFilter? LastActingFilter { get; private set; }

    public void ConfigureFailure(string code, string message)
    {
        _nextResult = AppResult<ApplyResult>.Fail(
            new AppError(400, code, message));
    }

    public void ConfigureSuccess(Guid? entityId = null)
    {
        _nextResult = AppResult<ApplyResult>.Success(
            new ApplyResult(
                entityId ?? Guid.NewGuid(),
                SourceReferenceTargetType.Artifact));
    }

    public Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal,
        ReviewBatch batch,
        VisibilityFilter actingFilter,
        CancellationToken ct)
    {
        LastActingFilter = actingFilter;
        var result = _nextResult ?? AppResult<ApplyResult>.Success(
            new ApplyResult(Guid.NewGuid(), SourceReferenceTargetType.Artifact));
        return Task.FromResult(result);
    }
}
