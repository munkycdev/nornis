using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

public class DigestAiClient : IDigestAiClient
{
    // options: empty apart from the schema — see ExtractionAiClient.
    // No output ceiling: these deployments rejected the one the old SDK sent.

    private readonly IAiCallBinder _binder;
    private readonly ILogger<DigestAiClient> _logger;

    public DigestAiClient(
        IAiCallBinder binder,
        ILogger<DigestAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    public async Task<DigestAiResponse> GenerateAsync(AiPromptRequest request, CancellationToken ct)
    {
        var callOptions = new AiCallOptions(new AiJsonSchema("world_digest", StructuredOutputSchema));

        var (content, usage) = await AiCallExecutor.ExecuteAsync(
                _binder, AiTask.Ask, request, callOptions, "World digest", _logger, ct);

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
