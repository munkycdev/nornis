namespace Nornis.Api.Contracts.Responses;

public record RetrospectiveResponse(
    int AssessedCount,
    int ProposedCount,
    Guid? ReviewBatchId);
