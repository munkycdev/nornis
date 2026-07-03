namespace Nornis.Api.Contracts.Requests;

public record BatchRejectRequest(IReadOnlyList<Guid> ProposalIds);
