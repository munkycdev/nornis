using System.Runtime.CompilerServices;
using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Prep's AI client, scripted: the text it streams, then a final chunk with the usage and the cut-off flag.</summary>
public sealed class FakePrepAiClient : IAdventureDraftingAiClient
{
    private string[] _texts = [];

    public List<AiPromptRequest> Requests { get; } = [];

    public AiUsage? Usage { get; set; }

    public bool CutOff { get; set; }

    public bool HangAfterText { get; set; }

    public Exception? ThrowAfterText { get; set; }

    /// <summary>Runs after the text and before the final chunk: something happening mid-draft.</summary>
    public Func<Task>? BeforeEnd { get; set; }

    public void Script(params string[] texts) => _texts = texts;

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        Requests.Add(request);
        foreach (var text in _texts)
        {
            await Task.Yield();
            yield return new AiStreamChunk { Text = text };
        }

        if (HangAfterText)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (ThrowAfterText is not null)
        {
            throw ThrowAfterText;
        }

        if (BeforeEnd is not null)
        {
            await BeforeEnd();
        }

        yield return new AiStreamChunk { Text = string.Empty, Usage = Usage, CutOff = CutOff };
    }
}
