using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeCheckInAiClient : ICheckInAiClient
{
    private readonly List<AiPromptRequest> _requests = [];
    private IReadOnlyList<CheckInPick> _picks = [];
    private Exception? _exception;

    public IReadOnlyList<AiPromptRequest> Requests => _requests.AsReadOnly();

    public void SetupPicks(params CheckInPick[] picks) => _picks = picks;

    public void SetupFailure(Exception exception) => _exception = exception;

    public Task<CheckInAiResponse> ChooseAsync(AiPromptRequest request, CancellationToken ct)
    {
        _requests.Add(request);

        if (_exception is not null)
        {
            throw _exception;
        }

        return Task.FromResult(new CheckInAiResponse
        {
            Picks = _picks,
            Usage = new AiUsage
            {
                InputTokens = 1200,
                OutputTokens = 400,
                TotalTokens = 1600,
                DurationMs = 1500,
                Model = "gpt-4o"
            }
        });
    }
}
