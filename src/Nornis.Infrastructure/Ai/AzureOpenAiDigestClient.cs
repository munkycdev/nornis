using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiDigestClient : IDigestAiClient
{
    // options: empty apart from the schema — see AzureOpenAiExtractionClient.
    // No output ceiling: these deployments rejected the one the old SDK sent.

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiDigestClient> _logger;

    public AzureOpenAiDigestClient(
        IChatClient chatClient,
        ILogger<AzureOpenAiDigestClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<DigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct)
    {
        var chatOptions = AzureOpenAiCallExecutor.JsonSchema("world_digest", StructuredOutputSchema, strict: true);

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, chatOptions, "World digest", _logger, ct);

        try
        {
            using var document = JsonDocument.Parse(content);
            var digest = document.RootElement.GetProperty("digest").GetString();

            if (string.IsNullOrWhiteSpace(digest))
            {
                throw new AiParseException("World digest response carried an empty digest.")
                {
                    Usage = usage
                };
            }

            return new DigestAiResponse
            {
                DigestMarkdown = digest,
                Usage = usage
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse world digest response");
            // Carry the tokens this attempt was billed for; the caller meters them.
            throw new AiParseException("Failed to parse world digest response.", ex)
            {
                Usage = usage
            };
        }
    }

    internal const string StructuredOutputSchema =
        """
        {
          "type": "object",
          "properties": {
            "digest": {
              "type": "string"
            }
          },
          "required": ["digest"],
          "additionalProperties": false
        }
        """;
}
