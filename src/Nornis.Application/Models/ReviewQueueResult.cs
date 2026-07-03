using Nornis.Domain.Entities;

namespace Nornis.Application.Models;

public record ReviewQueueResult(
    IReadOnlyList<ReviewProposal> Proposals,
    bool HasMore);
