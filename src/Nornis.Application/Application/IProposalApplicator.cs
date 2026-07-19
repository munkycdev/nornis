using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Application;

/// <summary>
/// Applies the proposed mutation to the knowledge graph.
/// </summary>
public interface IProposalApplicator
{
    /// <param name="actingFilter">
    /// What the reviewer accepting this proposal may see. Payloads that reference an
    /// artifact by name resolve through this filter, so a proposal can never bind to an
    /// artifact its accepter cannot see. GM-gated callers pass
    /// <see cref="VisibilityFilter.All"/>.
    /// </param>
    Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal,
        ReviewBatch batch,
        VisibilityFilter actingFilter,
        CancellationToken ct);
}

public record ApplyResult(
    Guid EntityId,
    SourceReferenceTargetType TargetType);
