using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeImageReadingClient : IImageReadingClient
{
    public string MarkdownToReturn { get; set; } = "## photo.png\n\nA banner bearing the sigil of House Voss.";
    public Exception? ExceptionToThrow { get; set; }
    public int CallCount { get; private set; }
    public ImageReadingRequest? LastRequest { get; private set; }

    public Task<ImageReadingResponse> ReadAsync(ImageReadingRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new ImageReadingResponse
        {
            Markdown = MarkdownToReturn,
            InputTokens = 1500,
            OutputTokens = 300,
            TotalTokens = 1800,
            DurationMs = 700,
            Model = request.Model
        });
    }
}
