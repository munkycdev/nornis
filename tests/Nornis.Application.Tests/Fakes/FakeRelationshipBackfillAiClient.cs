using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeRelationshipBackfillAiClient : IRelationshipBackfillAiClient
{
    public RelationshipBackfillAiResponse? Response { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public int CallCount { get; private set; }
    public RelationshipBackfillAiRequest? LastRequest { get; private set; }

    public Task<RelationshipBackfillAiResponse> ProposeLinksAsync(RelationshipBackfillAiRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(Response ?? new RelationshipBackfillAiResponse
        {
            Links = [],
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            DurationMs = 500,
            Model = request.Model
        });
    }
}
