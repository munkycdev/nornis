using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiArtifactSummaryClient : IArtifactSummaryAiClient
{
    // options: empty apart from the schema — see AzureOpenAiExtractionClient.
    // MaxOutputTokenCount serialises as max_tokens, which these deployments reject.

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiArtifactSummaryClient> _logger;

    public AzureOpenAiArtifactSummaryClient(
        ChatClient chatClient,
        ILogger<AzureOpenAiArtifactSummaryClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ArtifactSummaryAiResponse> SummarizeAsync(AiPromptRequest request, CancellationToken ct)
    {
        var completionOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "artifact_summary",
                jsonSchema: BinaryData.FromString(StructuredOutputSchema),
                jsonSchemaIsStrict: true)
        };

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, completionOptions, "Artifact summary", _logger, ct);

        try
        {
            using var document = JsonDocument.Parse(content);
            var summary = document.RootElement.GetProperty("summary").GetString();

            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new AiParseException("Artifact summary response carried an empty summary.")
                {
                    Usage = usage
                };
            }

            return new ArtifactSummaryAiResponse
            {
                Summary = summary,
                Usage = usage
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse artifact summary response");
            // Carry the tokens this attempt was billed for; the caller meters them.
            throw new AiParseException("Failed to parse artifact summary response.", ex)
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
            "summary": {
              "type": "string"
            }
          },
          "required": ["summary"],
          "additionalProperties": false
        }
        """;
}
