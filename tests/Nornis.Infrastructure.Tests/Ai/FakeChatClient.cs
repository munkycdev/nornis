using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// A hand-written <see cref="IChatClient"/> for the AI client tests. Each call takes the next
/// scripted behaviour; the last one repeats, so a single script covers any number of calls.
/// Every call's messages and options are recorded, which is how a test reads what a client sent.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<CancellationToken, Task<ChatResponse>>> _responses = new();
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> _streams = new();
    private readonly List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> _calls = [];

    public IReadOnlyList<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls => _calls;

    #region Scripting GetResponseAsync

    /// <summary>A completion carrying <paramref name="text"/> and the given usage.</summary>
    public FakeChatClient Returns(string text, long input = 500, long output = 150, long total = 650) =>
        Enqueue(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = total },
        }));

    public FakeChatClient Throws(Exception exception) =>
        Enqueue(_ => Task.FromException<ChatResponse>(exception));

    /// <summary>Never answers; waits until the call's token is cancelled.</summary>
    public FakeChatClient Hangs() =>
        Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

    private FakeChatClient Enqueue(Func<CancellationToken, Task<ChatResponse>> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    #endregion

    /// <summary>The SDK exception for an HTTP failure with <paramref name="status"/>.</summary>
    public static ClientResultException HttpFailure(int status)
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(status);
        return new ClientResultException(response);
    }

    #region Scripting GetStreamingResponseAsync

    /// <summary>
    /// Streams one update per text, then (when given) a usage-only update, then — when given —
    /// throws <paramref name="thenThrow"/>, or hangs until cancelled if <paramref name="thenHang"/>.
    /// </summary>
    public FakeChatClient Streams(
        IReadOnlyList<string> texts,
        UsageDetails? usage = null,
        Exception? thenThrow = null,
        bool thenHang = false)
    {
        _streams.Enqueue(ct => StreamUpdates(texts, usage, thenThrow, thenHang, ct));
        return this;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamUpdates(
        IReadOnlyList<string> texts,
        UsageDetails? usage,
        Exception? thenThrow,
        bool thenHang,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var text in texts)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        if (usage is not null)
        {
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(usage)] };
        }

        if (thenHang)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (thenThrow is not null)
        {
            throw thenThrow;
        }
    }

    #endregion

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((messages.ToList(), options));
        return Next(_responses, "GetResponseAsync")(cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _calls.Add((messages.ToList(), options));
        return Next(_streams, "GetStreamingResponseAsync")(cancellationToken);
    }

    private static T Next<T>(Queue<T> script, string method)
    {
        if (script.Count == 0)
        {
            throw new InvalidOperationException($"{method} was called but nothing was scripted for it.");
        }

        return script.Count > 1 ? script.Dequeue() : script.Peek();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
