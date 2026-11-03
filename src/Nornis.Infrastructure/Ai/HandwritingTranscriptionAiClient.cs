using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IHandwritingTranscriptionClient"/>. Shares
/// the extraction <see cref="IChatClient"/> (nornis-extract, gpt-5.4 — multimodal); sends
/// one user message with a text part followed by one image part per page, in order.
/// Plain-text response — transcription needs no structured output.
/// </summary>
public class HandwritingTranscriptionAiClient : IHandwritingTranscriptionClient
{

    private readonly IAiCallBinder _binder;
    private readonly ILogger<HandwritingTranscriptionAiClient> _logger;

    public HandwritingTranscriptionAiClient(
        IAiCallBinder binder, ILogger<HandwritingTranscriptionAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    public async Task<HandwritingTranscriptionResponse> TranscribeAsync(HandwritingTranscriptionRequest request, CancellationToken ct)
    {
        var parts = new List<AIContent>
        {
            new TextContent(
                $"Transcribe the following {request.Pages.Count} page(s) of handwritten notes, in order.")
        };
        foreach (var page in request.Pages)
        {
            parts.Add(new DataContent(page.ImageBytes, page.MediaType));
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt()),
            new ChatMessage(ChatRole.User, parts)
        };
        // options: none — see ExtractionAiClient on why there is no output ceiling.
        var (content, usage) = await AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Ask, request.Scope, request.Model, messages, options: null, request.TimeoutSeconds,
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
