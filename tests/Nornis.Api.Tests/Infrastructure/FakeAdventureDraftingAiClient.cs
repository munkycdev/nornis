using System.Runtime.CompilerServices;
using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Prep's drafting client for the API tests: streams <see cref="Text"/> in two pieces, then a
/// usage chunk, as the real client does. Every request is kept so a test can read the prompt.
/// </summary>
public class FakeAdventureDraftingAiClient : IAdventureDraftingAiClient
{
    private readonly List<AiPromptRequest> _requests = [];

    public string Text { get; set; } = "> Gulls wheel over the docks.";

    public IReadOnlyList<AiPromptRequest> Requests => _requests;

    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return StreamCoreAsync(Text, request.Model, ct);
    }

    private static async IAsyncEnumerable<AiStreamChunk> StreamCoreAsync(
        string text, string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var half = text.Length / 2;
        foreach (var piece in new[] { text[..half], text[half..] })
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return new AiStreamChunk { Text = piece };
        }

        yield return new AiStreamChunk
        {
            Text = string.Empty,
            Usage = new AiUsage { Model = model, InputTokens = 3_000, OutputTokens = 400, TotalTokens = 3_400, DurationMs = 900 },
        };
    }
}
