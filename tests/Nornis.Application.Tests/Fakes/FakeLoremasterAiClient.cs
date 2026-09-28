using System.Runtime.CompilerServices;
using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// A test double for <see cref="ILoremasterAiClient"/> that supports configurable
/// canned responses, failure modes, and call recording. Answers are streamed in a few
/// chunks followed by a usage chunk, the shape the real client delivers.
/// </summary>
public class FakeLoremasterAiClient : ILoremasterAiClient
{
    private readonly List<AiPromptRequest> _requests = [];
    private CannedLoremasterResponse? _cannedResponse;
    private FailureMode _failureMode = FailureMode.None;
    private IReadOnlyList<string>? _scriptedChunks;
    private AiUsage? _scriptedUsage;
    private Exception? _failureAfterChunks;
    private int _callCount;

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
    public int CallCount => _callCount;

    /// <summary>
    /// True once the stream the service was reading has been disposed, whether because the
    /// reader left or because the stream ran to its end.
    /// </summary>
    public bool StreamDisposed { get; private set; }

    /// <summary>
    /// Gets or sets the next response to return. If null, a default canned response is used.
    /// Retained for backward compatibility with existing tests.
    /// </summary>
    public CannedLoremasterResponse? NextResponse
    {
        get => _cannedResponse;
        set => _cannedResponse = value;
    }

    /// <summary>
    /// Gets or sets an exception to throw on the next call, before any text.
    /// Retained for backward compatibility. Prefer <see cref="SetupTimeout"/>,
    /// <see cref="SetupRateLimited"/>, or <see cref="SetupServiceError"/> for typed failures.
    /// </summary>
    public Exception? NextException { get; set; }

    /// <summary>
    /// Configures the fake to return a successful canned response.
    /// </summary>
    public void SetupSuccess(CannedLoremasterResponse response)
    {
        ClearSetup();
        _cannedResponse = response;
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

        SetupSuccess(new CannedLoremasterResponse
        {
            AnswerText = answerText,
            Usage = new AiUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                DurationMs = 200 + (outputTokens * 5), // Simulate ~5ms per output token
                Model = "gpt-4o"
            }
        });
    }

    /// <summary>
    /// Streams exactly these chunks, then the usage chunk when <paramref name="usage"/> is given.
    /// Null usage is a stream that finished without ever reporting what it cost.
    /// </summary>
    public void SetupChunks(IReadOnlyList<string> chunks, AiUsage? usage)
    {
        ClearSetup();
        _scriptedChunks = chunks;
        _scriptedUsage = usage;
    }

    /// <summary>
    /// Streams these chunks and then fails with <paramref name="failure"/>: a model call that
    /// dies partway through its answer. No usage chunk ever arrives.
    /// </summary>
    public void SetupFailureAfter(IReadOnlyList<string> chunks, Exception failure)
    {
        ClearSetup();
        _scriptedChunks = chunks;
        _failureAfterChunks = failure;
    }

    /// <summary>
    /// The model never answers: the stream waits until its token is cancelled.
    /// </summary>
    public void SetupHangUntilCancelled()
    {
        ClearSetup();
        _failureMode = FailureMode.Hang;
    }

    /// <summary>
    /// Configures the fake to simulate a timeout (throws <see cref="OperationCanceledException"/>).
    /// </summary>
    public void SetupTimeout()
    {
        ClearSetup();
        _failureMode = FailureMode.Timeout;
    }

    /// <summary>
    /// Configures the fake to simulate a rate limit (429) response
    /// (throws <see cref="AiHttpException"/> with status 429).
    /// </summary>
    public void SetupRateLimited()
    {
        ClearSetup();
        _failureMode = FailureMode.RateLimited;
    }

    /// <summary>
    /// Configures the fake to simulate a service error (5xx) response
    /// (throws <see cref="AiHttpException"/> with status 503).
    /// </summary>
    public void SetupServiceError()
    {
        ClearSetup();
        _failureMode = FailureMode.ServiceError;
    }

    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct)
    {
        // Recorded at the call, not on first read, so a call the service makes is counted even
        // if nothing ever enumerates it.
        _requests.Add(request);
        _callCount++;
        StreamDisposed = false;

        return StreamCoreAsync(ct);
    }

    private async IAsyncEnumerable<AiStreamChunk> StreamCoreAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            // Every failure below is thrown from the enumeration, as the interface promises,
            // never from the StreamAsync call itself.
            await Task.Yield();

            if (NextException is not null)
            {
                throw NextException;
            }

            switch (_failureMode)
            {
                case FailureMode.Timeout:
                    throw new OperationCanceledException(
                        "The AI request timed out after the configured timeout period.");

                case FailureMode.RateLimited:
                    throw new AiHttpException("AI service rate limited: HTTP 429", 429);

                case FailureMode.ServiceError:
                    throw new AiHttpException("AI service unavailable: HTTP 503", 503);

                case FailureMode.Hang:
                    await Task.Delay(Timeout.Infinite, ct);
                    break;
            }

            var chunks = _scriptedChunks is not null
                ? CannedLoremasterResponse.StreamChunks(
                    _scriptedChunks, _failureAfterChunks is null ? _scriptedUsage : null, ct)
                : (_cannedResponse ?? DefaultResponse()).AsStream(ct);

            await foreach (var chunk in chunks)
            {
                yield return chunk;
            }

            if (_failureAfterChunks is not null)
            {
                throw _failureAfterChunks;
            }
        }
        finally
        {
            StreamDisposed = true;
        }
    }

    /// <summary>
    /// Resets the client to its initial state: no failures, default response, cleared call history.
    /// </summary>
    public void Reset()
    {
        _requests.Clear();
        _callCount = 0;
        ClearSetup();
    }

    private void ClearSetup()
    {
        _cannedResponse = null;
        _failureMode = FailureMode.None;
        _scriptedChunks = null;
        _scriptedUsage = null;
        _failureAfterChunks = null;
        NextException = null;
    }

    private static CannedLoremasterResponse DefaultResponse() => new()
    {
        AnswerText = "I don't have a confirmed source for that yet.",
        Usage = new AiUsage
        {
            InputTokens = 150,
            OutputTokens = 42,
            TotalTokens = 192,
            DurationMs = 620,
            Model = "gpt-4o"
        }
    };

    private enum FailureMode
    {
        None,
        Timeout,
        RateLimited,
        ServiceError,
        Hang
    }
}
