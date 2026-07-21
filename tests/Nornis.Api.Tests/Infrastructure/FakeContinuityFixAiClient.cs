using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IContinuityFixAiClient"/> used by the continuity-fix integration
/// tests. Returns a configurable set of proposals and records how many times it was called.
/// </summary>
public class FakeContinuityFixAiClient : IContinuityFixAiClient
{
    private IReadOnlyList<ContinuityFixProposal> _proposals = [];
    private Exception? _exception;

    public int CallCount { get; private set; }

    public void SetupProposals(params ContinuityFixProposal[] proposals)
    {
        _proposals = proposals;
        _exception = null;
    }

    public void SetupFailure(Exception exception)
    {
        _exception = exception;
    }

    public Task<ContinuityFixAiResponse> DraftAsync(ContinuityFixAiRequest request, CancellationToken ct)
    {
        CallCount++;

        if (_exception is not null)
        {
            throw _exception;
        }

        return Task.FromResult(new ContinuityFixAiResponse
        {
            Proposals = _proposals,
            InputTokens = 700,
            OutputTokens = 90,
            TotalTokens = 790,
            DurationMs = 987,
            Model = "gpt-4o"
        });
    }
}
