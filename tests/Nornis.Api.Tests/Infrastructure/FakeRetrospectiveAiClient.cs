using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Default retrospective AI client for the test host: assesses nothing. Tests that
/// exercise the retrospective flow substitute their own via a derived factory.
/// </summary>
public class FakeRetrospectiveAiClient : IRetrospectiveAiClient
{
    public Task<RetrospectiveAiResponse> AssessAsync(RetrospectiveAiRequest request, CancellationToken ct) =>
        Task.FromResult(new RetrospectiveAiResponse
        {
            Verdicts = [],
            InputTokens = 0,
            OutputTokens = 0,
            TotalTokens = 0,
            DurationMs = 0,
            Model = request.Model
        });
}
