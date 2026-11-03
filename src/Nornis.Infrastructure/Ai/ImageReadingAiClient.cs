using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IImageReadingClient"/>. Shares the
/// extraction <see cref="IChatClient"/> (nornis-extract, gpt-5.4 — multimodal); sends one
/// user message with a text part naming the files followed by one image part per file.
/// Plain markdown out — the extraction pass downstream does the structuring.
/// </summary>
public class ImageReadingAiClient : IImageReadingClient
{

    private readonly IAiCallBinder _binder;
    private readonly ILogger<ImageReadingAiClient> _logger;

    public ImageReadingAiClient(
        IAiCallBinder binder, ILogger<ImageReadingAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    public async Task<ImageReadingResponse> ReadAsync(ImageReadingRequest request, CancellationToken ct)
    {
        var fileNames = string.Join(", ", request.Images.Select(i => i.FileName));
        var parts = new List<AIContent>
        {
            new TextContent(
                $"Read the following {request.Images.Count} image(s), in order: {fileNames}.")
        };
        foreach (var image in request.Images)
        {
            parts.Add(new DataContent(image.ImageBytes, image.MediaType));
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt()),
            new ChatMessage(ChatRole.User, parts)
        };
        // options: none — see ExtractionAiClient on why there is no output ceiling.
        var (content, usage) = await AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Extraction, request.Scope, request.Model, messages, options: null, request.TimeoutSeconds,
            "Image reading", _logger, ct);

        return new ImageReadingResponse
        {
            Markdown = content,
            Usage = usage
        };
    }

    internal static string BuildSystemPrompt()
    {
        return """
            You read images for Nornis, a tabletop-RPG world memory system. You receive one
            or more images attached to a source — artwork, screenshots, photographed
            handouts, diagrams, or scanned documents.

            For each image, produce a markdown section headed "## {filename}" containing:
            - Any legible text in the image, transcribed faithfully.
            - A concise description of what the image depicts, focused on lore: named
              people, places, factions, items, events, symbols, and their visible
              relationships.
            - Proper nouns matter most. Mark uncertain readings like [?Voss]; skip pure
              decoration.

            Do not invent names or facts that are not visibly supported. Return ONLY the
            markdown sections — no preamble, no commentary.
            """;
    }
}
