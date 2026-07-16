using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeHandwritingTranscriptionClient : IHandwritingTranscriptionClient
{
    public string MarkdownToReturn { get; set; } = "# Transcribed notes\n\nCaptain Voss was seen at the docks.";
    public Exception? ExceptionToThrow { get; set; }
    public int CallCount { get; private set; }
    public HandwritingTranscriptionRequest? LastRequest { get; private set; }

    public Task<HandwritingTranscriptionResponse> TranscribeAsync(HandwritingTranscriptionRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new HandwritingTranscriptionResponse
        {
            Markdown = MarkdownToReturn,
            InputTokens = 2000,
            OutputTokens = 400,
            TotalTokens = 2400,
            DurationMs = 800,
            Model = request.Model
        });
    }
}
