using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IHandwritingTranscriptionClient"/>. Shares
/// the extraction <see cref="ChatClient"/> (nornis-extract, gpt-5.4 — multimodal); sends
/// one user message with a text part followed by one image part per page, in order.
/// Plain-text response — transcription needs no structured output.
/// </summary>
public class AzureOpenAiHandwritingTranscriptionClient : IHandwritingTranscriptionClient
{

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiHandwritingTranscriptionClient> _logger;

    public AzureOpenAiHandwritingTranscriptionClient(
        ChatClient chatClient, ILogger<AzureOpenAiHandwritingTranscriptionClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<HandwritingTranscriptionResponse> TranscribeAsync(HandwritingTranscriptionRequest request, CancellationToken ct)
    {
        var parts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(
                $"Transcribe the following {request.Pages.Count} page(s) of handwritten notes, in order.")
        };
        foreach (var page in request.Pages)
        {
            parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(page.ImageBytes), page.MediaType));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(BuildSystemPrompt()),
            new UserChatMessage(parts)
        };
        // options: empty — see AzureOpenAiExtractionClient. MaxOutputTokenCount serialises as
        // "max_tokens", which these deployments reject with HTTP 400.
        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, messages, new ChatCompletionOptions(), request.Model, request.TimeoutSeconds,
            "Handwriting transcription", _logger, ct);

        return new HandwritingTranscriptionResponse
        {
            Markdown = content,
            Usage = usage
        };
    }

    internal static string BuildSystemPrompt()
    {
        return """
            You transcribe handwritten tabletop-RPG session notes for Nornis, a world memory
            system. You receive photographed or scanned pages of handwriting, in reading order.

            Produce a faithful markdown transcription:
            - Transcribe exactly what is written. Do not summarize, reorder, expand, or invent.
            - Preserve the visible structure: headings, lists, indentation, emphasis
              (underlines become bold), tables if drawn.
            - Proper nouns matter most — names of people, places, factions, and things. When a
              word is genuinely unreadable, write [illegible]; when unsure between readings,
              pick the likelier and mark it like [?Voss].
            - Ignore doodles, margins scribbles that carry no words, and crossed-out text.
            - Join the pages into one continuous document; do not add page headers.

            Return ONLY the markdown transcription — no preamble, no commentary.
            """;
    }
}
