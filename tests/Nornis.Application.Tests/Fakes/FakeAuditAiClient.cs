using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IAuditAiClient"/>. Returns a configurable set of findings, or throws
/// a configured exception, and records the requests it received.
/// </summary>
public class FakeAuditAiClient : IAuditAiClient
{
    private readonly List<AuditAiRequest> _requests = [];
    private IReadOnlyList<AuditFinding> _findings = [];
    private Exception? _exception;

    public IReadOnlyList<AuditAiRequest> Requests => _requests.AsReadOnly();
    public AuditAiRequest? LastRequest => _requests.Count > 0 ? _requests[^1] : null;
    public int CallCount { get; private set; }

    public int InputTokens { get; set; } = 900;
    public int OutputTokens { get; set; } = 120;
    public string Model { get; set; } = "gpt-4o";

    public void SetupFindings(params AuditFinding[] findings)
    {
        _findings = findings;
        _exception = null;
    }

    public void SetupFailure(Exception exception)
    {
        _exception = exception;
    }

    public Task<AuditAiResponse> AssessAsync(AuditAiRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        CallCount++;

        if (_exception is not null)
        {
            throw _exception;
        }

        return Task.FromResult(new AuditAiResponse
        {
            Findings = _findings,
            InputTokens = InputTokens,
            OutputTokens = OutputTokens,
            TotalTokens = InputTokens + OutputTokens,
            DurationMs = 1234,
            Model = Model
        });
    }
}
