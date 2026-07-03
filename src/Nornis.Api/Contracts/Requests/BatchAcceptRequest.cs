namespace Nornis.Api.Contracts.Requests;

public record BatchAcceptRequest(IReadOnlyList<Guid> ProposalIds);
