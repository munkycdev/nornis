using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Application;

/// <summary>
/// Applies the proposed mutation to the knowledge graph.
/// </summary>
public interface IProposalApplicator
{
    Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal,
        ReviewBatch batch,
        CancellationToken ct);
}

public record ApplyResult(
    Guid EntityId,
    SourceReferenceTargetType TargetType);
