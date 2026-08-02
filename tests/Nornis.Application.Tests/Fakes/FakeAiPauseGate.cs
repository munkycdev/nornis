using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Running unless a test says otherwise, which is how production spends its life.</summary>
public class FakeAiPauseGate : IAiPauseGate
{
    public AiPauseState State { get; set; } = AiPauseState.Running;

    public void Pause(string? reason = null) => State = new AiPauseState(true, reason);

    public Task<AiPauseState> GetAsync(CancellationToken ct) => Task.FromResult(State);
}
