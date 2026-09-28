using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The one shell around a chat completion: timeout enforcement, usage capture, and the
/// translation of SDK failures into the Application taxonomy (timeout / parse / HTTP).
/// Nine clients used to paste this preamble with three drifted exception vocabularies;
/// each client now contributes only its prompt, schema, and parse.
/// </summary>
public static class AzureOpenAiCallExecutor
{
    public static Task<(string Content, AiUsage Usage)> ExecuteAsync(
        IChatClient chatClient,
        AiPromptRequest request,
        ChatOptions? chatOptions,
        string operation,
        ILogger logger,
        CancellationToken ct) =>
        ExecuteAsync(
            chatClient, Messages(request), chatOptions, request.Model, request.TimeoutSeconds,
            operation, logger, ct);

    /// <summary>Message-list overload for the vision clients, whose user message carries image parts.</summary>
    public static async Task<(string Content, AiUsage Usage)> ExecuteAsync(
        IChatClient chatClient,
        IList<ChatMessage> messages,
        ChatOptions? chatOptions,
        string model,
        int timeoutSeconds,
        string operation,
        ILogger logger,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await chatClient.GetResponseAsync(messages, chatOptions, linkedCts.Token);
            stopwatch.Stop();

            // The SDK answered an empty completion by throwing on its first content part; this
            // keeps that outcome in the same arm of the taxonomy rather than handing each parser
            // an empty string to fail on in its own way.
            if (string.IsNullOrEmpty(response.Text))
            {
                throw new InvalidOperationException("The completion carried no content.");
            }

            return (response.Text, ToUsage(response.Usage, model, stopwatch));
        }
        catch (Exception ex) when (Translate(ex, timeoutCts, ct, timeoutSeconds, operation, logger) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// Streams a completion as text deltas, ending with one chunk that carries the usage. The
    /// timeout covers the whole answer, not the first token: a stream that stalls halfway is as
    /// stuck as a request that never starts. Failures surface from the enumeration in the same
    /// taxonomy <see cref="ExecuteAsync(IChatClient, AiPromptRequest, ChatOptions?, string, ILogger, CancellationToken)"/>
    /// uses — possibly after some text has already been yielded, which the caller must expect.
    /// </summary>
    public static async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        IChatClient chatClient,
        AiPromptRequest request,
        ChatOptions? chatOptions,
        string operation,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        UsageDetails? usage = null;
        IAsyncEnumerator<ChatResponseUpdate>? updates = null;
        try
        {
            while (true)
            {
                ChatResponseUpdate update;

                // C# forbids a yield inside a try that has a catch, so only the step that can
                // throw — starting the stream and pulling the next update — sits inside one.
                try
                {
                    updates ??= chatClient
                        .GetStreamingResponseAsync(Messages(request), chatOptions, linkedCts.Token)
                        .GetAsyncEnumerator(linkedCts.Token);

                    if (!await updates.MoveNextAsync())
                    {
                        break;
                    }

                    update = updates.Current;
                }
                catch (Exception ex) when (Translate(ex, timeoutCts, ct, request.TimeoutSeconds, operation, logger) is { } translated)
                {
                    throw translated;
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = usageContent.Details;
                    }
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return new AiStreamChunk { Text = update.Text };
                }
            }
        }
        finally
        {
            if (updates is not null)
            {
                await updates.DisposeAsync();
            }
        }

        stopwatch.Stop();

        // Null, not zeros, when the provider reported nothing: zeros would read as a free call,
        // and the caller could no longer tell "cost nothing" from "we were not told".
        yield return new AiStreamChunk
        {
            Text = string.Empty,
            Usage = usage is null ? null : ToUsage(usage, request.Model, stopwatch),
        };
    }

    /// <summary>
    /// Structured output against a hand-written schema, sent to the provider exactly as written.
    ///
    /// Deliberately not <c>ChatResponseFormat.ForJsonSchema</c>: the OpenAI adapter runs every
    /// schema given that way through its strict-mode transform — forcing
    /// <c>"additionalProperties": false</c> and marking every property required — whether or
    /// not strict mode was asked for. Extraction's schema is non-strict on purpose, with an open
    /// <c>proposedValue</c> object; transformed, the model could only ever fill it with <c>{}</c>.
    /// Setting the SDK's own response format keeps each schema byte-for-byte what its client wrote.
    /// </summary>
    public static ChatOptions JsonSchema(string schemaName, string schema, bool strict) => new()
    {
        // A factory, not a shared instance: the SDK mutates the options object it sends.
        RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions
        {
            ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: schemaName,
                jsonSchema: BinaryData.FromString(schema),
                jsonSchemaIsStrict: strict),
        },
    };

    private static List<ChatMessage> Messages(AiPromptRequest request) =>
    [
        new(ChatRole.System, request.SystemPrompt),
        new(ChatRole.User, request.UserMessage),
    ];

    private static AiUsage ToUsage(UsageDetails? usage, string model, Stopwatch stopwatch) => new()
    {
        Model = model,
        InputTokens = (int)(usage?.InputTokenCount ?? 0),
        CachedInputTokens = usage?.CachedInputTokenCount is { } cached ? (int)cached : null,
        OutputTokens = (int)(usage?.OutputTokenCount ?? 0),
        TotalTokens = (int)(usage?.TotalTokenCount ?? 0),
        DurationMs = (int)stopwatch.ElapsedMilliseconds,
    };

    /// <summary>
    /// Maps an SDK failure into the Application taxonomy, or returns null for a cancellation the
    /// caller asked for — that one propagates untouched, because nothing went wrong.
    /// </summary>
    private static Exception? Translate(
        Exception ex,
        CancellationTokenSource timeoutCts,
        CancellationToken ct,
        int timeoutSeconds,
        string operation,
        ILogger logger)
    {
        switch (ex)
        {
            case OperationCanceledException when timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested:
                logger.LogWarning("{Operation} AI call timed out after {TimeoutSeconds}s", operation, timeoutSeconds);
                return new AiTimeoutException($"{operation} AI call timed out after {timeoutSeconds} seconds.");

            case ClientResultException clientResult:
                logger.LogError(clientResult, "{Operation} AI call failed with status {Status}", operation, clientResult.Status);
                return new AiHttpException($"{operation} AI call failed: HTTP {clientResult.Status}", clientResult.Status, clientResult);

            case OperationCanceledException:
                return null;

            default:
                logger.LogError(ex, "Unexpected error during {Operation} AI call", operation);
                return new AiHttpException($"Unexpected error during {operation} AI call.", null, ex);
        }
    }
}
