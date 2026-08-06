using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeArtifactSummaryAiClient : IArtifactSummaryAiClient
{
    public string SummaryToReturn { get; set; } = "Captain Voss is a smuggler operating out of Black Harbor.";
    public Exception? ExceptionToThrow { get; set; }
    public int CallCount { get; private set; }
    public AiPromptRequest? LastRequest { get; private set; }

    public Task<ArtifactSummaryAiResponse> SummarizeAsync(AiPromptRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new ArtifactSummaryAiResponse
        {
            Summary = SummaryToReturn,
            Usage = new AiUsage
            {
                Model = "gpt-4o",
                InputTokens = 400,
                OutputTokens = 60,
                TotalTokens = 460,
                DurationMs = 800
            }
        });
    }
}
