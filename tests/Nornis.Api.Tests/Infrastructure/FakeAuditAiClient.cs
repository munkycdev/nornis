using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IAuditAiClient"/> used by the continuity-audit integration tests.
/// Returns a configurable set of findings and records how many times it was called.
/// </summary>
public class FakeAuditAiClient : IAuditAiClient
{
    private IReadOnlyList<AuditFinding> _findings = [];
    private Exception? _exception;

    public int CallCount { get; private set; }

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
        CallCount++;

        if (_exception is not null)
        {
            throw _exception;
        }

        return Task.FromResult(new AuditAiResponse
        {
            Findings = _findings,
            InputTokens = 900,
            OutputTokens = 120,
            TotalTokens = 1020,
            DurationMs = 1234,
            Model = "gpt-4o"
        });
    }
}
