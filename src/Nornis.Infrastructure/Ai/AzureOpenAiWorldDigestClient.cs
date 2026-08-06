using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiWorldDigestClient : IWorldDigestAiClient
{
    // options: empty apart from the schema — see AzureOpenAiExtractionClient.
    // MaxOutputTokenCount serialises as max_tokens, which these deployments reject.

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiWorldDigestClient> _logger;

    public AzureOpenAiWorldDigestClient(
        ChatClient chatClient,
        ILogger<AzureOpenAiWorldDigestClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<WorldDigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct)
    {
        var completionOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "world_digest",
                jsonSchema: BinaryData.FromString(StructuredOutputSchema),
                jsonSchemaIsStrict: true)
        };

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, completionOptions, "World digest", _logger, ct);

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

            return new WorldDigestAiResponse
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
