using System.Net;
using System.Runtime.CompilerServices;
using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// A fake implementation of <see cref="ILoremasterAiClient"/> for integration testing.
/// Supports configurable canned responses, failure modes, and call recording. Answers
/// stream in a few chunks and end with a usage chunk, the shape the real client delivers.
/// </summary>
public class FakeLoremasterAiClient : ILoremasterAiClient
{
    private readonly List<AiPromptRequest> _requests = [];
    private CannedAnswer? _cannedResponse;
    private FailureMode _failureMode = FailureMode.None;

    /// <summary>
    /// All requests that have been made to this client, in order.
    /// </summary>
    public IReadOnlyList<AiPromptRequest> Requests => _requests.AsReadOnly();

    /// <summary>
    /// The most recent request made to this client, or null if none.
    /// </summary>
    public AiPromptRequest? LastRequest => _requests.Count > 0 ? _requests[^1] : null;

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

        _cannedResponse = new CannedAnswer(answerText, new AiUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            DurationMs = 200 + (outputTokens * 5),
            Model = "gpt-4o"
        });
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

    /// <summary>
    /// Streams <paramref name="partialText"/> and then fails with a service error: a model call
    /// that dies partway through its answer, after the response has already started.
    /// </summary>
    public void SetupServiceErrorAfter(string partialText)
    {
        _failureMode = FailureMode.ServiceErrorAfterText;
        _cannedResponse = new CannedAnswer(partialText, Usage: null);
    }

    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        return StreamCoreAsync(_failureMode, _cannedResponse ?? DefaultResponse(), ct);
    }

    private static async IAsyncEnumerable<AiStreamChunk> StreamCoreAsync(
        FailureMode failureMode,
        CannedAnswer answer,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();

        switch (failureMode)
        {
            case FailureMode.Timeout:
                throw new OperationCanceledException(
                    "The AI request timed out after the configured timeout period.");

            case FailureMode.ServiceError:
                throw ServiceUnavailable();
        }

        foreach (var chunk in Split(answer.Text))
        {
            ct.ThrowIfCancellationRequested();
            yield return new AiStreamChunk { Text = chunk };
        }

        if (failureMode == FailureMode.ServiceErrorAfterText)
        {
            throw ServiceUnavailable();
        }

        yield return new AiStreamChunk { Text = string.Empty, Usage = answer.Usage };
    }

    private static HttpRequestException ServiceUnavailable() =>
        new("AI service unavailable: HTTP 503", inner: null, statusCode: HttpStatusCode.ServiceUnavailable);

    /// <summary>Up to three contiguous pieces, cut without regard to [ref:…] markers.</summary>
    private static IEnumerable<string> Split(string text)
    {
        var size = Math.Max(1, (int)Math.Ceiling(text.Length / 3.0));
        for (var start = 0; start < text.Length; start += size)
        {
            yield return text.Substring(start, Math.Min(size, text.Length - start));
        }
    }

    private static CannedAnswer DefaultResponse() => new(
        "I don't have a confirmed source for that yet.",
        new AiUsage
        {
            InputTokens = 150,
            OutputTokens = 42,
            TotalTokens = 192,
            DurationMs = 620,
            Model = "gpt-4o"
        });

    private sealed record CannedAnswer(string Text, AiUsage? Usage);

    private enum FailureMode
    {
        None,
        Timeout,
        ServiceError,
        ServiceErrorAfterText
    }
}
