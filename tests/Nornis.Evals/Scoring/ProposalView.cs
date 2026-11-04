using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nornis.Evals.Scoring;

/// <summary>
/// One extraction proposal as the eval reads it: what kind of change, which artifact, and
/// the words a reviewer would read. Built from the proposal's JSON as the run saved it, with
/// ids resolved to names through <paramref name="names"/>, so two models that point at the same
/// artifact one by id and one by name still meet.
/// </summary>
public sealed partial record ProposalView(
    string Kind,
    string ChangeType,
    string Subject,
    string? Other,
    string? Label,
    string? Text,
    string? ArtifactType,
    string? Visibility,
    string? TruthState,
    decimal? Confidence)
{
    /// <summary>What a match must agree on, beyond the subject: an artifact, a fact about it, a
    /// link between two, or a merge.</summary>
    public const string ArtifactKind = "artifact";
    public const string FactKind = "fact";
    public const string LinkKind = "link";
    public const string MergeKind = "merge";

    public static ProposalView Read(JsonElement proposal, IReadOnlyDictionary<Guid, string> names)
    {
        var change = String(proposal, "changeType") ?? "";
        var value = proposal.TryGetProperty("proposedValue", out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
        var target = Guid.TryParse(String(proposal, "targetId"), out var id) ? id : (Guid?)null;
        string Named(Guid? artifact, string? fallback) =>
            artifact is { } key && names.TryGetValue(key, out var name) ? name : fallback ?? "(unknown)";

        var confidence = proposal.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : (decimal?)null;
        var visibility = String(value, "visibility");
        var truth = String(value, "truthState");
        return change switch
        {
            "CreateArtifact" => new(ArtifactKind, change, String(value, "name") ?? "(unnamed)", null, null,
                String(value, "summary"), String(value, "type"), visibility, null, confidence),
            "UpdateArtifact" => new(ArtifactKind, change, Named(target, String(value, "name")), null, String(value, "status"),
                String(value, "summary"), null, visibility, null, confidence),
            "MergeArtifact" => new(MergeKind, change, Named(target, String(value, "name")),
                Named(Guid.TryParse(String(value, "sourceArtifactId"), out var duplicate) ? duplicate : null, null),
                null, String(value, "summary"), null, visibility, null, confidence),
            "AddFact" => new(FactKind, change, Named(target, String(value, "artifactName")), null, String(value, "predicate"),
                String(value, "value"), null, visibility, truth, confidence),
            "UpdateFact" => FactUpdate(target, names, value, change, visibility, truth, confidence),
            "AddRelationship" => new(LinkKind, change,
                Named(Guid.TryParse(String(value, "artifactAId"), out var a) ? a : null, String(value, "artifactAName")),
                Named(Guid.TryParse(String(value, "artifactBId"), out var b) ? b : null, String(value, "artifactBName")),
                String(value, "type"), String(value, "description"), null, visibility, truth, confidence),
            _ => new(LinkKind, change, Named(target, null), null, String(value, "type"), String(value, "description"),
                null, visibility, truth, confidence),
        };
    }

    /// <summary>An UpdateFact names only the fact's id; the replay names facts
    /// <c>Artifact · predicate</c>, so the subject and label come back from that.</summary>
    private static ProposalView FactUpdate(
        Guid? target, IReadOnlyDictionary<Guid, string> names, JsonElement value, string change, string? visibility, string? truth, decimal? confidence)
    {
        var fact = target is { } key && names.TryGetValue(key, out var name) ? name : "(unknown) · (unknown)";
        var split = fact.Split(" · ", 2);
        return new(FactKind, change, split[0], null, split.Length > 1 ? split[1] : null, String(value, "value"), null, visibility, truth, confidence);
    }

    /// <summary>The line a reviewer reads on the baseline checklist and in the report.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        switch (Kind)
        {
            case ArtifactKind:
                text.Append(ChangeType == "CreateArtifact" ? "**New**" : "**Update**").Append(' ').Append(Subject);
                var details = new[] { ArtifactType, Visibility, Label }.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (details.Count > 0)
                {
                    text.Append(" (").Append(string.Join(", ", details)).Append(')');
                }

                break;
            case FactKind:
                text.Append(ChangeType == "AddFact" ? "**Fact**" : "**Fact update**").Append(' ').Append(Subject)
                    .Append(" · ").Append(Label ?? "(no predicate)");
                break;
            case MergeKind:
                text.Append("**Merge** ").Append(Other).Append(" into ").Append(Subject);
                break;
            default:
                text.Append("**Link** ").Append(Subject).Append(" → ").Append(Label ?? "?").Append(" → ").Append(Other);
                break;
        }

        if (!string.IsNullOrWhiteSpace(Text))
        {
            text.Append(": ").Append(OneLine(Text));
        }

        if (Kind != ArtifactKind && Kind != MergeKind && (Visibility is not null || TruthState is not null))
        {
            text.Append(" _(").Append(string.Join(", ", new[] { TruthState, Visibility }.Where(s => s is not null))).Append(")_");
        }

        return text.ToString();
    }

    /// <summary>Lowercase words without articles or punctuation: "The Salt Factor" and
    /// "salt-factor" are one name.</summary>
    public static string Normalize(string? text) =>
        string.Join(' ', Words(text).Where(w => w is not ("the" or "a" or "an")));

    /// <summary>The words of a text, lowercased, for comparing what two proposals say.</summary>
    public static IEnumerable<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : NotWord().Split(text.ToLowerInvariant()).Where(w => w.Length > 0);

    private static string OneLine(string text)
    {
        var line = string.Join(' ', text.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length > 240 ? line[..240] + "…" : line;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotWord();
}
