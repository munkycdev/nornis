using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Default digest AI client for the test host, serving both the world digest and the campaign
/// recap. Returns fixed markdown so endpoints activate and authorization can be exercised;
/// tests that care about generated content substitute their own via a derived factory.
/// </summary>
public class FakeDigestAiClient : IDigestAiClient
{
    public Task<DigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct) =>
        Task.FromResult(new DigestAiResponse
        {
            DigestMarkdown = "## The story so far\nNothing of note.",
            Usage = new AiUsage
            {
                InputTokens = 0,
                OutputTokens = 0,
                TotalTokens = 0,
                DurationMs = 0,
                Model = request.Model
            }
        });
}
