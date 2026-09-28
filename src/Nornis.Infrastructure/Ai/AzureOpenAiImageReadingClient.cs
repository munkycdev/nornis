using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Azure OpenAI implementation of <see cref="IImageReadingClient"/>. Shares the
/// extraction <see cref="IChatClient"/> (nornis-extract, gpt-5.4 — multimodal); sends one
/// user message with a text part naming the files followed by one image part per file.
/// Plain markdown out — the extraction pass downstream does the structuring.
/// </summary>
public class AzureOpenAiImageReadingClient : IImageReadingClient
{

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiImageReadingClient> _logger;

    public AzureOpenAiImageReadingClient(
        IChatClient chatClient, ILogger<AzureOpenAiImageReadingClient> logger)
    {
        _chatClient = chatClient;
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
        // options: none — see AzureOpenAiExtractionClient on why there is no output ceiling.
        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, messages, chatOptions: null, request.Model, request.TimeoutSeconds,
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
