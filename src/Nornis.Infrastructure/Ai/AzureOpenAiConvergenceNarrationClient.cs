using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiConvergenceNarrationClient : IConvergenceNarrationClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiConvergenceNarrationClient> _logger;

    public AzureOpenAiConvergenceNarrationClient(
        ChatClient chatClient,
        ILogger<AzureOpenAiConvergenceNarrationClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ConvergenceNarrationAiResponse> NarrateAsync(AiPromptRequest request, CancellationToken ct)
    {
        var completionOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "convergence_narration",
                jsonSchema: BinaryData.FromString(StructuredOutputSchema),
                jsonSchemaIsStrict: true)
        };

        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, completionOptions, "Convergence narration", _logger, ct);

        try
        {
            using var document = JsonDocument.Parse(content);
            var narrations = new List<ConvergenceNarration>();

            foreach (var element in document.RootElement.GetProperty("narrations").EnumerateArray())
            {
                var rationale = element.GetProperty("rationale").GetString();

                // A model-authored id that is not a Guid is dropped here rather than carried:
                // the caller matches by id, and an unmatchable entry would silently annotate
                // nothing while looking like it worked.
                if (Guid.TryParse(element.GetProperty("candidateId").GetString(), out var candidateId)
                    && !string.IsNullOrWhiteSpace(rationale))
                {
                    narrations.Add(new ConvergenceNarration(candidateId, rationale));
                }
            }

            return new ConvergenceNarrationAiResponse
            {
                Narrations = narrations,
                Usage = usage
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to parse convergence narration response");
            // Carry the tokens this attempt was billed for; the caller meters them.
            throw new AiParseException("Failed to parse convergence narration response.", ex)
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
            "narrations": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "candidateId": {
                    "type": "string"
                  },
                  "rationale": {
                    "type": "string"
                  }
                },
                "required": ["candidateId", "rationale"],
                "additionalProperties": false
              }
            }
          },
          "required": ["narrations"],
          "additionalProperties": false
        }
        """;
}
