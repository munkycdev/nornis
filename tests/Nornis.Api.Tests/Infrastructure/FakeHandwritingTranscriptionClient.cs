using Nornis.Application.Ai;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Default handwriting transcription client for the test host. Returns fixed markdown so the
/// transcribe endpoint activates and its authorization can be exercised; the production
/// registration is a throwing stub when Azure OpenAI is unconfigured, which would turn a
/// test asserting 403 into one asserting 500.
///
/// Mutable so a test can arrange a blank reading or a failure without a derived factory —
/// the endpoint has distinct behaviour for both.
/// </summary>
public class FakeHandwritingTranscriptionClient : IHandwritingTranscriptionClient
{
    public string MarkdownToReturn { get; set; } = "# Session 4\n\nCaptain Voss was seen at Black Harbor.";

    public Exception? ExceptionToThrow { get; set; }

    public int CallCount { get; private set; }

    public Task<HandwritingTranscriptionResponse> TranscribeAsync(
        HandwritingTranscriptionRequest request, CancellationToken ct)
    {
        CallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new HandwritingTranscriptionResponse
        {
            Markdown = MarkdownToReturn,
            Usage = new AiUsage
            {
                InputTokens = 2000,
                OutputTokens = 400,
                TotalTokens = 2400,
                DurationMs = 800,
                Model = request.Model
            }
        });
    }
}
