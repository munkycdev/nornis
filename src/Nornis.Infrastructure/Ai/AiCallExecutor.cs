using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The one shell around a chat completion: binding the call to whoever pays for it, timeout
/// enforcement, usage capture, and the translation of SDK failures into the Application taxonomy
/// (timeout / parse / HTTP). Nine clients used to paste this preamble with three drifted
/// exception vocabularies; each client now contributes only its prompt, schema, and parse.
///
/// The binding happens here, on every call, and nowhere else (feature 39): a client names the
/// task its work belongs to and the model Nornis's own key would use, and the binder decides
/// which key and which model actually run it.
/// </summary>
public static class AiCallExecutor
{
    public static Task<(string Content, AiUsage Usage)> ExecuteAsync(
        IAiCallBinder binder,
        AiTask task,
        AiPromptRequest request,
        AiCallOptions? options,
        string operation,
        ILogger logger,
        CancellationToken ct) =>
        ExecuteAsync(
            binder, task, request.Scope, request.Model, Messages(request), options, request.TimeoutSeconds,
            operation, logger, ct);

    /// <summary>Message-list overload for the vision clients, whose user message carries image parts.</summary>
    public static async Task<(string Content, AiUsage Usage)> ExecuteAsync(
        IAiCallBinder binder,
        AiTask task,
        AiCallScope scope,
        string nornisModel,
        IList<ChatMessage> messages,
        AiCallOptions? options,
        int timeoutSeconds,
        string operation,
        ILogger logger,
        CancellationToken ct)
    {
        // Only the stream reads a search's pages, so a whole-response call can't ask for one.
        if (options?.WebSearch is not null)
        {
            throw new NotSupportedException("Web search runs only on a streamed call (StreamAsync), which reads its pages.");
        }

        var binding = await binder.BindAsync(scope, task, nornisModel, CarriesImages(messages), needsWebSearch: false, ct);
        var chatClient = binding.Client;
        var chatOptions = binding.Traits.ChatOptions(options);
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

            await binder.ReportAsync(binding, null, CancellationToken.None);
            return (response.Text, ToUsage(response.Usage, binding.Funding, stopwatch));
        }
        catch (Exception ex) when (Translate(ex, binding, timeoutCts, ct, timeoutSeconds, operation, logger) is { } translated)
        {
            await binder.ReportAsync(binding, translated, CancellationToken.None);
            throw translated;
        }
    }

    /// <summary>
    /// Streams a completion as text deltas, ending with one chunk that carries the usage. The
    /// timeout covers the whole answer, not the first token: a stream that stalls halfway is as
    /// stuck as a request that never starts. Failures surface from the enumeration in the same
    /// taxonomy <c>ExecuteAsync</c>
    /// uses — possibly after some text has already been yielded, which the caller must expect.
    /// </summary>
    public static async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        IAiCallBinder binder,
        AiTask task,
        AiPromptRequest request,
        AiCallOptions? options,
        string operation,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var binding = await binder.BindAsync(request.Scope, task, request.Model, needsImages: false, needsWebSearch: options?.WebSearch is not null, ct);
        var chatClient = binding.Client;
        var chatOptions = binding.Traits.ChatOptions(options);
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        UsageDetails? usage = null;
        var cutOff = false;

        // Only a research turn (feature 40) reads the stream for its searching.
        var research = options?.WebSearch is null ? null : new WebResearchCollector();
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
                catch (Exception ex) when (Translate(ex, binding, timeoutCts, ct, request.TimeoutSeconds, operation, logger, research is not null) is { } translated)
                {
                    await binder.ReportAsync(binding, translated, CancellationToken.None);
                    throw translated;
                }

                if (research is not null)
                {
                    binding.Traits.ReadWebResearch(update, research);
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = usageContent.Details;
                    }
                }

                if (update.FinishReason == ChatFinishReason.Length)
                {
                    cutOff = true;
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
        await binder.ReportAsync(binding, null, CancellationToken.None);

        // A provider that stops at the ceiling without saying why still reports what it spent:
        // all of the ceiling means the answer was cut off (feature 41).
        if (options?.MaxOutputTokens is { } ceiling && usage?.OutputTokenCount >= ceiling)
        {
            cutOff = true;
        }

        // Null, not zeros, when the provider reported nothing: zeros would read as a free call,
        // and the caller could no longer tell "cost nothing" from "we were not told".
        yield return new AiStreamChunk
        {
            Text = string.Empty,
            Usage = usage is null ? null : ToUsage(usage, binding.Funding, stopwatch) with { WebSearches = research?.Searches },
            CutOff = cutOff,
            WebResearch = research?.ToResult(),
        };
    }

    private static List<ChatMessage> Messages(AiPromptRequest request) =>
    [
        new(ChatRole.System, request.SystemPrompt),
        new(ChatRole.User, request.UserMessage),
    ];

    /// <summary>Whether any message carries an image, which only some models can read.</summary>
    internal static bool CarriesImages(IEnumerable<ChatMessage> messages) =>
        messages.Any(m => m.Contents.Any(c => c is DataContent data && data.HasTopLevelMediaType("image")
                                              || c is UriContent uri && uri.HasTopLevelMediaType("image")));

    private static AiUsage ToUsage(UsageDetails? usage, AiFunding funding, Stopwatch stopwatch) => new()
    {
        Model = funding.Model,
        Funding = funding,
        InputTokens = (int)(usage?.InputTokenCount ?? 0),
        CachedInputTokens = usage?.CachedInputTokenCount is { } cached ? (int)cached : null,
        OutputTokens = (int)(usage?.OutputTokenCount ?? 0),
        ReasoningTokens = usage?.ReasoningTokenCount is { } reasoning ? (int)reasoning : null,
        TotalTokens = (int)(usage?.TotalTokenCount ?? 0),
        DurationMs = (int)stopwatch.ElapsedMilliseconds,
    };

    /// <summary>
    /// Maps an SDK failure into the Application taxonomy, or returns null for a cancellation the
    /// caller asked for — that one propagates untouched, because nothing went wrong.
    /// </summary>
    private static Exception? Translate(
        Exception ex,
        AiBinding binding,
        CancellationTokenSource timeoutCts,
        CancellationToken ct,
        int timeoutSeconds,
        string operation,
        ILogger logger,
        bool searched = false)
    {
        switch (ex)
        {
            case not OperationCanceledException and { } when searched && binding.Traits.RefusedWebSearch(ex):
                logger.LogWarning(ex, "{Operation} AI call was refused: {Provider} won't search the web on {Model}.",
                    operation, binding.Funding.Provider, binding.Funding.Model);
                return new AiWebSearchUnsupportedException(ex);

            case OperationCanceledException when timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested:
                logger.LogWarning("{Operation} AI call timed out after {TimeoutSeconds}s", operation, timeoutSeconds);
                return new AiTimeoutException($"{operation} AI call timed out after {timeoutSeconds} seconds.");

            case OperationCanceledException:
                return null;

            case { } when binding.Traits.TranslateFailure(ex, binding.Funding, operation, logger) is { } providerFailure:
                return providerFailure;

            default:
                logger.LogError(ex, "Unexpected error during {Operation} AI call", operation);
                return new AiHttpException($"Unexpected error during {operation} AI call.", null, ex);
        }
    }
}
