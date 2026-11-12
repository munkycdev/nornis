using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The check-in chooser (feature 44): one strict structured-output call on the Ask model that picks
/// the few candidates worth asking and phrases them. Same shape as <see cref="RetrospectiveAiClient"/>:
/// the binder decides who pays, the schema is closed and fully required so every provider's strict
/// mode accepts it, and parsing hands raw strings back for the service to resolve.
/// </summary>
public class CheckInAiClient : ICheckInAiClient
{
    private readonly IAiCallBinder _binder;
    private readonly ILogger<CheckInAiClient> _logger;

    public CheckInAiClient(IAiCallBinder binder, ILogger<CheckInAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    public async Task<CheckInAiResponse> ChooseAsync(AiPromptRequest request, CancellationToken ct)
    {
        var callOptions = new AiCallOptions(new AiJsonSchema("check_in_picks", GetStructuredOutputSchema()));

        var (content, usage) = await AiCallExecutor.ExecuteAsync(
            _binder, AiTask.Ask, request, callOptions, "Check-in", _logger, ct);

        return new CheckInAiResponse
        {
            Picks = ParsePicks(content),
            Usage = usage
        };
    }

    internal static string GetStructuredOutputSchema()
    {
        return """
            {
              "type": "object",
              "properties": {
                "picks": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "candidate": { "type": "string" },
                      "question": { "type": "string" },
                      "why": { "type": "string" },
                      "suggestedRule": { "type": ["string", "null"] },
                      "recordOnArtifactId": { "type": ["string", "null"] }
                    },
                    "required": ["candidate", "question", "why", "suggestedRule", "recordOnArtifactId"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["picks"],
              "additionalProperties": false
            }
            """;
    }

    internal static IReadOnlyList<CheckInPick> ParsePicks(string content)
    {
        var document = JsonNode.Parse(content)
            ?? throw new AiParseException("Check-in AI response was null or empty.");

        if (document["picks"] is not JsonArray picksArray)
        {
            throw new AiParseException("Check-in AI response missing 'picks' array.");
        }

        var picks = new List<CheckInPick>(picksArray.Count);

        foreach (var node in picksArray)
        {
            if (node is null)
                continue;

            var candidate = node["candidate"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            picks.Add(new CheckInPick
            {
                CandidateKey = candidate,
                Question = node["question"]?.GetValue<string>() ?? string.Empty,
                Why = node["why"]?.GetValue<string>() ?? string.Empty,
                SuggestedRule = node["suggestedRule"]?.GetValue<string>(),
                RecordOnArtifactId = node["recordOnArtifactId"]?.GetValue<string>()
            });
        }

        return picks;
    }
}
