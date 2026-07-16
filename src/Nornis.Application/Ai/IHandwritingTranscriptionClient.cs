namespace Nornis.Application.Ai;

/// <summary>
/// Vision transcription for handwritten notes: ordered page images in, markdown out.
/// Runs on the extraction ChatClient (gpt-5.4 is multimodal) before normal extraction.
/// </summary>
public interface IHandwritingTranscriptionClient
{
    Task<HandwritingTranscriptionResponse> TranscribeAsync(HandwritingTranscriptionRequest request, CancellationToken ct);
}

/// <summary>One page image, in reading order.</summary>
public record TranscriptionPage(byte[] ImageBytes, string MediaType);

public class HandwritingTranscriptionRequest
{
    public required IReadOnlyList<TranscriptionPage> Pages { get; init; }
    public required string Model { get; init; }
    public required int TimeoutSeconds { get; init; }
}

public class HandwritingTranscriptionResponse
{
    public required string Markdown { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int DurationMs { get; init; }
    public required string Model { get; init; }
}
