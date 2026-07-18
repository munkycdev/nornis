namespace Nornis.Application.Ai;

/// <summary>
/// Vision lore-reading for Image/Upload attachments: images in, markdown out. Distinct
/// from <see cref="IHandwritingTranscriptionClient"/> — transcription reproduces
/// writing faithfully; reading describes what an image shows and extracts its text.
/// </summary>
public interface IImageReadingClient
{
    Task<ImageReadingResponse> ReadAsync(ImageReadingRequest request, CancellationToken ct);
}

public sealed record ImageToRead(byte[] ImageBytes, string MediaType, string FileName);

public class ImageReadingRequest
{
    public required IReadOnlyList<ImageToRead> Images { get; init; }

    public required string Model { get; init; }

    public required int TimeoutSeconds { get; init; }
}

public class ImageReadingResponse
{
    public required string Markdown { get; init; }

    public required int InputTokens { get; init; }

    public required int OutputTokens { get; init; }

    public required int TotalTokens { get; init; }

    public required int DurationMs { get; init; }

    public required string Model { get; init; }
}
