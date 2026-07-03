using System.Net;
using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// A test double for <see cref="ILoremasterAiClient"/> that supports configurable
/// canned responses, failure modes, and call recording.
/// </summary>
public class FakeLoremasterAiClient : ILoremasterAiClient
{
    private readonly List<LoremasterAiRequest> _requests = [];
    private LoremasterAiResponse? _cannedResponse;
    private FailureMode _failureMode = FailureMode.None;
    private int _callCount;

    /// <summary>
    /// All requests that have been made to this client, in order.
    /// </summary>
    public IReadOnlyList<LoremasterAiRequest> Requests => _requests.AsReadOnly();

    /// <summary>
    /// The most recent request made to this client, or null if none.
    /// </summary>
    public LoremasterAiRequest? LastRequest => _requests.Count > 0 ? _requests[^1] : null;

    /// <summary>
    /// The number of calls made to this client.
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Gets or sets the next response to return. If null, a default canned response is used.
    /// Retained for backward compatibility with existing tests.
    /// </summary>
    public LoremasterAiResponse? NextResponse
    {
        get => _cannedResponse;
        set => _cannedResponse = value;
    }

    /// <summary>
    /// Gets or sets an exception to throw on the next call.
    /// Retained for backward compatibility. Prefer <see cref="SetupTimeout"/>,
    /// <see cref="SetupRateLimited"/>, or <see cref="SetupServiceError"/> for typed failures.
    /// </summary>
    public Exception? NextException { get; set; }

    /// <summary>
    /// Configures the fake to return a successful canned response.
    /// </summary>
    public void SetupSuccess(LoremasterAiResponse response)
    {
        _cannedResponse = response;
        _failureMode = FailureMode.None;
        NextException = null;
    }

    /// <summary>
    /// Configures the fake to return a successful response with the specified answer text
    /// and realistic token counts based on text length.
    /// </summary>
    public void SetupSuccess(string answerText)
    {
        // Simulate realistic token counts: ~4 characters per token for English text
        var outputTokens = Math.Max(10, answerText.Length / 4);
        var inputTokens = outputTokens * 3; // System prompt + context typically 3x larger than output

        _cannedResponse = new LoremasterAiResponse
        {
            AnswerText = answerText,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            DurationMs = 200 + (outputTokens * 5), // Simulate ~5ms per output token
            Model = "gpt-4o"
        };
        _failureMode = FailureMode.None;
        NextException = null;
    }

    /// <summary>
    /// Configures the fake to simulate a timeout (throws <see cref="OperationCanceledException"/>).
    /// </summary>
    public void SetupTimeout()
    {
        _failureMode = FailureMode.Timeout;
        _cannedResponse = null;
        NextException = null;
    }

    /// <summary>
    /// Configures the fake to simulate a rate limit (429) response
    /// (throws <see cref="HttpRequestException"/> with status 429).
    /// </summary>
    public void SetupRateLimited()
    {
        _failureMode = FailureMode.RateLimited;
        _cannedResponse = null;
        NextException = null;
    }

    /// <summary>
    /// Configures the fake to simulate a service error (5xx) response
    /// (throws <see cref="HttpRequestException"/> with status 503).
    /// </summary>
    public void SetupServiceError()
    {
        _failureMode = FailureMode.ServiceError;
        _cannedResponse = null;
        NextException = null;
    }

    public Task<LoremasterAiResponse> AskAsync(LoremasterAiRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        _callCount++;

        // Check for legacy NextException first (backward compat)
        if (NextException is not null)
        {
            throw NextException;
        }

        // Check configured failure mode
        switch (_failureMode)
        {
            case FailureMode.Timeout:
                throw new OperationCanceledException(
                    "The AI request timed out after the configured timeout period.");

            case FailureMode.RateLimited:
                throw new HttpRequestException(
                    "AI service rate limited: HTTP 429",
                    inner: null,
                    statusCode: HttpStatusCode.TooManyRequests);

            case FailureMode.ServiceError:
                throw new HttpRequestException(
                    "AI service unavailable: HTTP 503",
                    inner: null,
                    statusCode: HttpStatusCode.ServiceUnavailable);
        }

        // Return configured response or default
        return Task.FromResult(_cannedResponse ?? DefaultResponse());
    }

    /// <summary>
    /// Resets the client to its initial state: no failures, default response, cleared call history.
    /// </summary>
    public void Reset()
    {
        _requests.Clear();
        _callCount = 0;
        _cannedResponse = null;
        _failureMode = FailureMode.None;
        NextException = null;
    }

    private static LoremasterAiResponse DefaultResponse() => new()
    {
        AnswerText = "I don't have a confirmed source for that yet.",
        InputTokens = 150,
        OutputTokens = 42,
        TotalTokens = 192,
        DurationMs = 620,
        Model = "gpt-4o"
    };

    private enum FailureMode
    {
        None,
        Timeout,
        RateLimited,
        ServiceError
    }
}
