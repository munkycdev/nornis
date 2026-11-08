using System.Text.Json;
using System.Text.Json.Nodes;
using Nornis.Domain.Models;

namespace Nornis.Application.Ai;

/// <summary>
/// The one place an Incidental proposal is dropped (feature 43). Runs on a parsed, validated
/// response before any ReviewProposal is written, in the Worker and in the eval runner alike,
/// so the two count the same answer the same way.
/// <para>
/// The rule: every Incidental proposal goes, except a CreateArtifact that a kept proposal names
/// (<c>artifactName</c>, <c>artifactAName</c>, <c>artifactBName</c>). Without that exception a
/// kept Core fact would arrive at accept time naming an artifact nothing proposed, and the
/// reviewer would meet the near-match ladder for a thing the model had in fact proposed. Names
/// are compared with <see cref="ArtifactNameKey"/>, the policy dedup and accept-time resolution
/// already use. One pass suffices: a promoted create references nothing itself.
/// </para>
/// </summary>
public static class ExtractionTriage
{
    private static readonly string[] ReferenceFields = ["artifactName", "artifactAName", "artifactBName"];

    public static TriageResult Apply(IReadOnlyList<ExtractionProposal> proposals)
    {
        if (!proposals.Any(p => p.Significance == ProposalSignificance.Incidental))
        {
            return new TriageResult(proposals, [], 0);
        }

        var referencedNames = proposals
            .Where(p => p.Significance != ProposalSignificance.Incidental)
            .SelectMany(ReferencedNames)
            .ToList();

        var kept = new List<ExtractionProposal>(proposals.Count);
        var droppedCreates = new List<string>();

        foreach (var proposal in proposals)
        {
            if (proposal.Significance != ProposalSignificance.Incidental)
            {
                kept.Add(proposal);
                continue;
            }

            var createdName = proposal.ChangeType == "CreateArtifact" ? ProposedName(proposal) : null;
            if (createdName is not null && referencedNames.Any(n => ArtifactNameKey.AreEquivalent(n, createdName)))
            {
                kept.Add(proposal);
                continue;
            }

            if (createdName is not null)
            {
                droppedCreates.Add(createdName);
            }
        }

        return new TriageResult(kept, droppedCreates, proposals.Count - kept.Count);
    }

    private static IEnumerable<string> ReferencedNames(ExtractionProposal proposal)
    {
        if (Payload(proposal.ProposedValue) is not { } payload)
        {
            yield break;
        }

        foreach (var field in ReferenceFields)
        {
            if (StringField(payload, field) is { } name)
            {
                yield return name;
            }
        }
    }

    private static string? ProposedName(ExtractionProposal proposal) =>
        Payload(proposal.ProposedValue) is { } payload ? StringField(payload, "name") : null;

    /// <summary>The payload as JSON, however the proposal was built: the client hands over a
    /// <see cref="JsonElement"/>, tests hand over anonymous objects.</summary>
    private static JsonObject? Payload(object proposedValue)
    {
        try
        {
            var json = proposedValue is JsonElement element
                ? element.GetRawText()
                : JsonSerializer.Serialize(proposedValue);

            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            // A malformed payload is the validator's problem; triage keeps its hands off it.
            return null;
        }
    }

    private static string? StringField(JsonObject payload, string field)
    {
        var key = payload.Select(kv => kv.Key)
            .FirstOrDefault(k => string.Equals(k, field, StringComparison.OrdinalIgnoreCase));
        if (key is null || payload[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

/// <summary>
/// What triage kept, in the model's order, and what it let go: the count, and the names of
/// the CreateArtifacts among them, so "where did X go" has an answer in the log.
/// </summary>
public sealed record TriageResult(
    IReadOnlyList<ExtractionProposal> Kept,
    IReadOnlyList<string> DroppedCreateNames,
    int DroppedCount);
