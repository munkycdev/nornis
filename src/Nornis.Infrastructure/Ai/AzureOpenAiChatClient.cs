using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using SdkChatMessage = OpenAI.Chat.ChatMessage;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The <see cref="IChatClient"/> every AI client here is handed: Microsoft.Extensions.AI's OpenAI
/// adapter, except that streams are read straight from the OpenAI SDK.
///
/// Why: Azure ends every stream with a content-filter chunk whose choice carries no
/// <c>delta</c>, and the adapter (Microsoft.Extensions.AI.OpenAI 10.10.1, and upstream main as of
/// 2026-09-28) probes that chunk for <c>reasoning_content</c> without checking it is there —
/// "requires an element of type 'Object', but the target element has type 'Null'" — so every
/// streamed answer failed on its last chunk, after all of its text had arrived. The SDK itself
/// reads the chunk fine. <c>AzureOpenAiChatClientTests</c> replays that stream through both; when
/// its adapter-throws test starts failing, the adapter is fixed and this override can go.
/// </summary>
public sealed class AzureOpenAiChatClient : DelegatingChatClient
{
    private readonly ChatClient _sdk;

    public AzureOpenAiChatClient(ChatClient sdk)
        : base(sdk.AsIChatClient())
    {
        _sdk = sdk;
    }

    /// <summary>
    /// Text in, text and usage out — what a streamed answer needs. Options are honoured only
    /// through <see cref="ChatOptions.RawRepresentationFactory"/>, the one way the clients here set
    /// them (see <see cref="AzureOpenAiCallExecutor.JsonSchema"/>); anything else is refused rather
    /// than quietly dropped.
    /// </summary>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options is not null && options.RawRepresentationFactory is null)
        {
            throw new NotSupportedException(
                "Streaming takes its options through RawRepresentationFactory only.");
        }

        var sdkOptions = options?.RawRepresentationFactory?.Invoke(this) as ChatCompletionOptions ?? new();

        await foreach (var update in _sdk.CompleteChatStreamingAsync(ToSdkMessages(messages), sdkOptions, cancellationToken))
        {
            var contents = new List<AIContent>();
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    contents.Add(new TextContent(part.Text));
                }
            }

            if (update.Usage is { } usage)
            {
                contents.Add(new UsageContent(new UsageDetails
                {
                    InputTokenCount = usage.InputTokenCount,
                    OutputTokenCount = usage.OutputTokenCount,
                    TotalTokenCount = usage.TotalTokenCount,
                    CachedInputTokenCount = usage.InputTokenDetails?.CachedTokenCount,
                }));
            }

            if (contents.Count > 0)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, contents);
            }
        }
    }

    private static List<SdkChatMessage> ToSdkMessages(IEnumerable<AiChatMessage> messages) =>
        messages.Select(m =>
        {
            if (m.Contents.Any(c => c is not TextContent))
            {
                throw new NotSupportedException("Streaming carries text messages only.");
            }

            SdkChatMessage converted = m.Role == ChatRole.System ? new SystemChatMessage(m.Text)
                : m.Role == ChatRole.User ? new UserChatMessage(m.Text)
                : m.Role == ChatRole.Assistant ? new AssistantChatMessage(m.Text)
                : throw new NotSupportedException($"Streaming does not carry {m.Role} messages.");
            return converted;
        }).ToList();
}
