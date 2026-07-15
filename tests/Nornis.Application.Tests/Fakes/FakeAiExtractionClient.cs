using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeAiExtractionClient : IAiExtractionClient
{
    private readonly List<ExtractionRequest> _requests = [];
    private readonly Queue<Func<AiExtractionResponse>> _script = new();
    private AiExtractionResponse? _successResponse;
    private Exception? _transientException;
    private bool _parseFailure;
    private int _callCount;

    public IReadOnlyList<ExtractionRequest> Requests => _requests.AsReadOnly();
    public int CallCount => _callCount;

    /// <summary>
    /// Enqueues one call's behavior. Scripted calls take precedence over the Setup* modes,
    /// letting tests express sequences like "throw once, then succeed".
    /// </summary>
    public void EnqueueThrow(Exception exception) => _script.Enqueue(() => throw exception);

    /// <inheritdoc cref="EnqueueThrow" />
    public void EnqueueSuccess(AiExtractionResponse response) => _script.Enqueue(() => response);

    /// <summary>
    /// Configures the fake to return a successful response on each call.
    /// </summary>
    public void SetupSuccess(AiExtractionResponse response)
    {
        _successResponse = response;
        _transientException = null;
        _parseFailure = false;
    }

    /// <summary>
    /// Configures the fake to throw a transient exception on each call.
    /// </summary>
    public void SetupTransientFailure(Exception exception)
    {
        _transientException = exception;
        _successResponse = null;
        _parseFailure = false;
    }

    /// <summary>
    /// Configures the fake to return a response that will fail schema validation.
    /// Returns proposals with invalid ChangeType values.
    /// </summary>
    public void SetupParseFailure()
    {
        _parseFailure = true;
        _transientException = null;
        _successResponse = null;
    }

    public Task<AiExtractionResponse> ExtractAsync(ExtractionRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        _callCount++;

        if (_script.Count > 0)
        {
            return Task.FromResult(_script.Dequeue()());
        }

        if (_transientException is not null)
        {
            throw _transientException;
        }

        if (_parseFailure)
        {
            // Return a response with invalid proposals that will fail validation
            var invalidResponse = new AiExtractionResponse
            {
                Proposals =
                [
                    new ExtractionProposal
                    {
                        ChangeType = "InvalidChangeType",
                        TargetType = "InvalidTargetType",
                        ProposedValue = new { },
                        Rationale = "This has invalid enum values",
                        Confidence = 0.5m
                    }
                ],
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                DurationMs = 500,
                Model = "gpt-4o"
            };
            return Task.FromResult(invalidResponse);
        }

        if (_successResponse is not null)
        {
            return Task.FromResult(_successResponse);
        }

        throw new InvalidOperationException(
            "FakeAiExtractionClient has not been configured. Call SetupSuccess, SetupTransientFailure, or SetupParseFailure.");
    }
}
