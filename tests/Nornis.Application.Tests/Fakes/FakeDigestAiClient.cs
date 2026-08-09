using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeDigestAiClient : IDigestAiClient
{
    public string DigestToReturn { get; set; } = "## Active storylines\n- The Missing Caravan is advancing.";
    public Exception? ExceptionToThrow { get; set; }
    public List<AiPromptRequest> Requests { get; } = [];

    public Task<DigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct)
    {
        Requests.Add(request);

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new DigestAiResponse
        {
            DigestMarkdown = DigestToReturn,
            Usage = new AiUsage
            {
                Model = "gpt-4o",
                InputTokens = 2000,
                OutputTokens = 400,
                TotalTokens = 2400,
                DurationMs = 4000
            }
        });
    }
}
