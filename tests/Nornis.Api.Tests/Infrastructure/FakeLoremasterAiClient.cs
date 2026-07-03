using System.Net;
using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// A fake implementation of <see cref="ILoremasterAiClient"/> for integration testing.
/// Supports configurable canned responses, failure modes, and call recording.
/// </summary>
public class FakeLoremasterAiClient : ILoremasterAiClient
{
    private readonly List<LoremasterAiRequest> _requests = [];
    private LoremasterAiResponse? _cannedResponse;
    private FailureMode _failureMode = FailureMode.None;

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
    public int CallCount => _requests.Count;

    /// <summary>
    /// Configures the fake to return a successful response with the specified answer text
    /// and realistic token counts.
    /// </summary>
    public void SetupSuccess(string answerText)
    {
        var outputTokens = Math.Max(10, answerText.Length / 4);
        var inputTokens = outputTokens * 3;

        _cannedResponse = new LoremasterAiResponse
        {
            AnswerText = answerText,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            DurationMs = 200 + (outputTokens * 5),
            Model = "gpt-4o"
        };
        _failureMode = FailureMode.None;
    }

    /// <summary>
    /// Configures the fake to simulate a timeout.
    /// </summary>
    public void SetupTimeout()
    {
        _failureMode = FailureMode.Timeout;
        _cannedResponse = null;
    }

    /// <summary>
    /// Configures the fake to simulate a service error (5xx).
    /// </summary>
    public void SetupServiceError()
    {
        _failureMode = FailureMode.ServiceError;
        _cannedResponse = null;
    }

    public Task<LoremasterAiResponse> AskAsync(LoremasterAiRequest request, CancellationToken ct)
    {
        _requests.Add(request);

        switch (_failureMode)
        {
            case FailureMode.Timeout:
                throw new OperationCanceledException(
                    "The AI request timed out after the configured timeout period.");

            case FailureMode.ServiceError:
                throw new HttpRequestException(
                    "AI service unavailable: HTTP 503",
                    inner: null,
                    statusCode: HttpStatusCode.ServiceUnavailable);
        }

        return Task.FromResult(_cannedResponse ?? DefaultResponse());
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
        ServiceError
    }
}
