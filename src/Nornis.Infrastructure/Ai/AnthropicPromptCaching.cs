using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Marks the end of the system prompt as a cache breakpoint (feature 39 Phase C). OpenAI caches
/// a stable prefix by itself; Anthropic caches only up to a breakpoint the request names, so
/// without this every extraction would pay full price for the same long instructions. Nornis's
/// prompts put their stable part in the system message and the per-call part after it, so one
/// breakpoint there is the whole of the work; a prefix shorter than the model's minimum is
/// simply not cached.
/// </summary>
public sealed class AnthropicPromptCaching(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(WithBreakpoint(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(WithBreakpoint(messages), options, cancellationToken);

    /// <summary>The same messages, the last system message's last part carrying the breakpoint.
    /// The caller's list is left alone: clients reuse theirs.</summary>
    internal static List<ChatMessage> WithBreakpoint(IEnumerable<ChatMessage> messages)
    {
        var list = messages.ToList();
        var index = list.FindLastIndex(m => m.Role == ChatRole.System && m.Contents.Count > 0);
        if (index < 0)
        {
            return list;
        }

        var system = list[index];
        var contents = system.Contents.ToList();
        contents[^1] = contents[^1] switch
        {
            TextContent text => new TextContent(text.Text).WithCacheControl(new CacheControlEphemeral()),
            var other => other,
        };

        list[index] = new ChatMessage(ChatRole.System, contents) { AuthorName = system.AuthorName };
        return list;
    }
}
