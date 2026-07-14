using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeRetrospectiveAiClient : IRetrospectiveAiClient
{
    private readonly List<RetrospectiveAiRequest> _requests = [];
    private IReadOnlyList<RetrospectiveVerdict> _verdicts = [];
    private Exception? _exception;

    public IReadOnlyList<RetrospectiveAiRequest> Requests => _requests.AsReadOnly();

    public void SetupVerdicts(params RetrospectiveVerdict[] verdicts) => _verdicts = verdicts;

    public void SetupFailure(Exception exception) => _exception = exception;

    public Task<RetrospectiveAiResponse> AssessAsync(RetrospectiveAiRequest request, CancellationToken ct)
    {
        _requests.Add(request);

        if (_exception is not null)
        {
            throw _exception;
        }

        return Task.FromResult(new RetrospectiveAiResponse
        {
            Verdicts = _verdicts,
            InputTokens = 800,
            OutputTokens = 300,
            TotalTokens = 1100,
            DurationMs = 900,
            Model = "gpt-4o"
        });
    }
}
