using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Default check-in chooser for the test host: picks nothing, so a started check-in holds every
/// candidate under "More to consider". Tests that need picks substitute their own.
/// </summary>
public class FakeCheckInAiClient : ICheckInAiClient
{
    public Task<CheckInAiResponse> ChooseAsync(AiPromptRequest request, CancellationToken ct) =>
        Task.FromResult(new CheckInAiResponse
        {
            Picks = [],
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
