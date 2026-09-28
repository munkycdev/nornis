using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiArtifactSummaryClient : IArtifactSummaryAiClient
{
    // options: empty apart from the schema — see AzureOpenAiExtractionClient.
    // No output ceiling: these deployments rejected the one the old SDK sent.

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiArtifactSummaryClient> _logger;

    public AzureOpenAiArtifactSummaryClient(
        IChatClient chatClient,
        ILogger<AzureOpenAiArtifactSummaryClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ArtifactSummaryAiResponse> SummarizeAsync(AiPromptRequest request, CancellationToken ct)
    {
        var chatOptions = AzureOpenAiCallExecutor.JsonSchema("artifact_summary", StructuredOutputSchema, strict: true);

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, chatOptions, "Artifact summary", _logger, ct);

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
