using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Tests.Fakes;

public class FakeProposalApplicator : IProposalApplicator
{
    private AppResult<ApplyResult>? _nextResult;
    private AppError? _firstAttemptError;
    private readonly HashSet<Guid> _attempted = [];

    /// <summary>The filter the last ApplyAsync call was made with — lets callers assert
    /// that name resolution runs through the accepting reviewer's eyes.</summary>
    public VisibilityFilter? LastActingFilter { get; private set; }

    /// <summary>Every proposal id ApplyAsync was called for, in call order. Lets callers
    /// assert both the order the service applied proposals in and that a retry pass never
    /// re-applies something that already succeeded.</summary>
    public List<Guid> AppliedProposalIds { get; } = [];

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

    /// <summary>
    /// Fail each proposal's FIRST apply with the given error and succeed on any later one —
    /// a name reference that only resolves once something else in the batch has landed.
    /// </summary>
    public void ConfigureFailFirstAttemptPerProposal(int statusCode, string code, string message)
    {
        _firstAttemptError = new AppError(statusCode, code, message);
    }

    public Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal,
        ReviewBatch batch,
        Source source,
        VisibilityFilter actingFilter,
        CancellationToken ct)
    {
        LastActingFilter = actingFilter;
        AppliedProposalIds.Add(proposal.Id);

        if (_firstAttemptError is not null && _attempted.Add(proposal.Id))
            return Task.FromResult(AppResult<ApplyResult>.Fail(_firstAttemptError));

        var result = _nextResult ?? AppResult<ApplyResult>.Success(
            new ApplyResult(Guid.NewGuid(), SourceReferenceTargetType.Artifact));
        return Task.FromResult(result);
    }
}
