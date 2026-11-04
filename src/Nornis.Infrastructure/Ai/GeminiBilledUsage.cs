using Microsoft.Extensions.AI;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Gemini reports thinking tokens apart from its output tokens (<c>thoughtsTokenCount</c> beside
/// <c>candidatesTokenCount</c>), and Google bills them at the output price; OpenAI and Anthropic
/// count them inside the output. Folding them in here keeps <see cref="UsageDetails.OutputTokenCount"/>
/// meaning "billed output" for every provider, so Costs doesn't undercount a thinking model.
/// <see cref="UsageDetails.ReasoningTokenCount"/> is left as reported.
/// </summary>
public sealed class GeminiBilledUsage(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // The same details object may be both the response's usage and a message's content;
        // folding it twice would double the thinking.
        var seen = new HashSet<UsageDetails>(ReferenceEqualityComparer.Instance);
        foreach (var usage in response.Messages.SelectMany(m => m.Contents).OfType<UsageContent>().Select(u => u.Details)
                     .Prepend(response.Usage))
        {
            if (usage is not null && seen.Add(usage))
            {
                Bill(usage);
            }
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            BillContents(update.Contents);
            yield return update;
        }
    }

    private static void BillContents(IEnumerable<AIContent> contents)
    {
        foreach (var usage in contents.OfType<UsageContent>())
        {
            Bill(usage.Details);
        }
    }

    internal static void Bill(UsageDetails? usage)
    {
        if (usage?.ReasoningTokenCount is { } thoughts and > 0)
        {
            usage.OutputTokenCount = (usage.OutputTokenCount ?? 0) + thoughts;
        }
    }
}
