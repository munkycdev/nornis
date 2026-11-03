using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

public class ArtifactSummaryAiClient : IArtifactSummaryAiClient
{
    // options: empty apart from the schema — see ExtractionAiClient.
    // No output ceiling: these deployments rejected the one the old SDK sent.

    private readonly IAiCallBinder _binder;
    private readonly ILogger<ArtifactSummaryAiClient> _logger;

    public ArtifactSummaryAiClient(
        IAiCallBinder binder,
        ILogger<ArtifactSummaryAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    public async Task<ArtifactSummaryAiResponse> SummarizeAsync(AiPromptRequest request, CancellationToken ct)
    {
        var callOptions = new AiCallOptions(new AiJsonSchema("artifact_summary", StructuredOutputSchema));

        var (content, usage) = await AiCallExecutor.ExecuteAsync(
                _binder, AiTask.Extraction, request, callOptions, "Artifact summary", _logger, ct);

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
