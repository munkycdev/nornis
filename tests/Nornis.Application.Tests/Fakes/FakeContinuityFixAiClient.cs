using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Fake <see cref="IContinuityFixAiClient"/>: returns configurable proposals, records calls
/// and the last request, and can be primed to throw.
/// </summary>
public class FakeContinuityFixAiClient : IContinuityFixAiClient
{
    public IReadOnlyList<ContinuityFixProposal> Proposals { get; set; } = [];
    public Exception? ExceptionToThrow { get; set; }
    public int CallCount { get; private set; }
    public AiPromptRequest? LastRequest { get; private set; }

    public Task<ContinuityFixAiResponse> DraftAsync(AiPromptRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new ContinuityFixAiResponse
        {
            Proposals = Proposals,
            Usage = new AiUsage
            {
                InputTokens = 500,
                OutputTokens = 80,
                TotalTokens = 580,
                DurationMs = 321,
                Model = request.Model
            }
        });
    }
}
