using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nornis.Web.Services;

/// <summary>How a proposal field is edited.</summary>
public enum ProposalFieldKind
{
    /// <summary>One line: a name, a label.</summary>
    Text,

    /// <summary>Several lines: a summary, a fact's value, a description.</summary>
    LongText,

    /// <summary>One of a fixed set: a type, a truth state, who can see it.</summary>
    Choice,
}

/// <summary>
/// One field of a proposal as Review's Edit shows it.
/// </summary>
/// <param name="Key">The payload's JSON property, in the casing the API writes (camelCase).</param>
/// <param name="Label">What the field is called on screen.</param>
/// <param name="Options">For a <see cref="ProposalFieldKind.Choice"/>: the values and their labels.</param>
/// <param name="Required">
/// The payload has no meaning without it, so it cannot be emptied. An optional field left empty
/// is written as null, which every payload reads as "not given".
/// </param>
/// <param name="OnlyIfPresent">
/// Shown only when the payload already carries it. A name that stands in for an id is there
/// only on proposals made in the same batch as the entry they refer to; offering it on the
/// others would let the reviewer add a reference the applicator then prefers over the real id.
/// </param>
public sealed record ProposalField(
    string Key,
    string Label,
    ProposalFieldKind Kind,
    IReadOnlyList<(string Value, string Label)>? Options = null,
    bool Required = false,
    bool OnlyIfPresent = false,
    string? Help = null);

/// <summary>
/// Review's Edit as a form (3.32). It used to open the proposal's JSON with the note "It must be
/// valid JSON", which is the first thing a new GM meets in their first session. Each proposal
/// type has a short list of the fields a person would change: the name, the type, the summary,
/// the fact. Everything else in the payload (ids, map coordinates, the confidence) is carried
/// through untouched, and the JSON is still there behind "Advanced".
///
/// The API validates whatever is saved (ReviewService.EditAsync), so this is the form, not the
/// rule. The value lists mirror the Domain enums the Web cannot reference; ProposalEditFormTests
/// reads their source and requires them to agree.
/// </summary>
public static class ProposalEditForm
{
    /// <summary>Mirrors <c>ArtifactType</c>.</summary>
    public static readonly IReadOnlyList<string> ArtifactTypes =
        ["Character", "Location", "Item", "Faction", "Event", "Storyline", "Concept", "Document"];

    /// <summary>Mirrors <c>ArtifactStatus</c>.</summary>
    public static readonly IReadOnlyList<string> ArtifactStatuses = ["Active", "Dormant", "Resolved", "Archived"];

    /// <summary>Mirrors <c>TruthState</c>.</summary>
    public static readonly IReadOnlyList<string> TruthStates = ["Confirmed", "Likely", "Rumor", "Disputed", "False", "Hidden"];

    private static readonly IReadOnlyList<(string, string)> TypeOptions = ArtifactTypes.Select(t => (t, t)).ToList();
    private static readonly IReadOnlyList<(string, string)> StatusOptions = ArtifactStatuses.Select(s => (s, s)).ToList();
    private static readonly IReadOnlyList<(string, string)> TruthOptions = TruthStates.Select(t => (t, t)).ToList();

    private static readonly IReadOnlyList<(string, string)> VisibilityOptions =
        VisibilityDisplay.Scopes.Select(s => (s, VisibilityDisplay.Label(s))).ToList();

    private static readonly ProposalField Visibility = new("visibility", "Who can see it", ProposalFieldKind.Choice, VisibilityOptions);
    private static readonly ProposalField Truth = new("truthState", "How sure the record is", ProposalFieldKind.Choice, TruthOptions);
    private static readonly ProposalField Summary = new("summary", "Summary", ProposalFieldKind.LongText);

    /// <summary>
    /// The fields Edit shows for a change type, in the order they are read. Empty for a type
    /// this form does not know, which leaves the reviewer the JSON as before.
    /// </summary>
    public static IReadOnlyList<ProposalField> FieldsFor(string changeType) => changeType switch
    {
        "CreateArtifact" =>
        [
            new("name", "Name", ProposalFieldKind.Text, Required: true),
            new("type", "Type", ProposalFieldKind.Choice, TypeOptions, Required: true),
            Summary,
            Visibility,
        ],
        "UpdateArtifact" =>
        [
            new("name", "Name", ProposalFieldKind.Text, Help: "Leave empty to keep the entry's name."),
            Summary,
            new("status", "Status", ProposalFieldKind.Choice, StatusOptions),
            Visibility,
        ],
        "MergeArtifact" =>
        [
            new("name", "Name of the entry that remains", ProposalFieldKind.Text, Help: "Leave empty to keep its name."),
            Summary,
            Visibility,
        ],
        "AddFact" =>
        [
            new("artifactName", "Entry", ProposalFieldKind.Text, OnlyIfPresent: true,
                Help: "The entry this fact is about, by name."),
            new("predicate", "What the fact is about", ProposalFieldKind.Text, Required: true,
                Help: "A short label, such as \"office location\"."),
            new("value", "Fact", ProposalFieldKind.LongText, Required: true),
            Truth,
            Visibility,
        ],
        "UpdateFact" =>
        [
            new("value", "Fact", ProposalFieldKind.LongText, Help: "Leave empty to keep the fact's wording."),
            Truth,
            Visibility,
        ],
        "AddRelationship" =>
        [
            new("artifactAName", "From", ProposalFieldKind.Text, OnlyIfPresent: true),
            new("type", "Relationship", ProposalFieldKind.Text, Required: true,
                Help: "How the first is related to the second, such as \"LocatedIn\" or \"AlliedWith\"."),
            new("artifactBName", "To", ProposalFieldKind.Text, OnlyIfPresent: true),
            new("description", "Description", ProposalFieldKind.LongText),
            Truth,
            Visibility,
        ],
        "UpdateRelationship" =>
        [
            new("type", "Relationship", ProposalFieldKind.Text, Help: "Leave empty to keep it."),
            new("description", "Description", ProposalFieldKind.LongText),
            Truth,
            Visibility,
        ],
        "AddPlacemark" =>
        [
            new("artifactName", "Entry", ProposalFieldKind.Text, OnlyIfPresent: true),
            new("label", "Label on the map", ProposalFieldKind.Text),
        ],
        _ => [],
    };

    /// <summary>
    /// What a change type is called in Review: "New entry", not "Create Artifact". A type this
    /// class does not know reads as its own name in words.
    /// </summary>
    public static string ChangeLabel(string changeType) => changeType switch
    {
        "CreateArtifact" => "New entry",
        "UpdateArtifact" => "Update entry",
        "MergeArtifact" => "Merge entries",
        "AddFact" => "New fact",
        "UpdateFact" => "Update fact",
        "AddRelationship" => "New relationship",
        "UpdateRelationship" => "Update relationship",
        "AddPlacemark" => "New map pin",
        _ => Sentence(changeType),
    };

    /// <summary>
    /// One payload property as a Review row shows it, in the words Edit uses for the same
    /// field: "Who can see it: GM only", not "visibility: GMOnly". A property the form does not
    /// show keeps its value and reads as its name in words.
    /// </summary>
    public static (string Label, string Value) Describe(string changeType, string key, string value)
    {
        var field = FieldsFor(changeType).FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return (Sentence(key), value);
        }

        var option = field.Options?.Where(o => o.Value == value).Select(o => o.Label).FirstOrDefault();
        return (field.Label, option ?? value);
    }

    // "sourceArtifactId" and "CreateArtifact" as "Source artifact id" and "Create artifact".
    private static string Sentence(string identifier)
    {
        var words = DisplayText.Humanize(identifier).ToLowerInvariant();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>
    /// The form's starting values, or null when the payload is not something a form can show
    /// (not a JSON object, or a type with no fields), in which case Edit opens on the JSON.
    /// A field the payload lacks, or holds as null or as anything but text, reads as empty;
    /// a field marked <see cref="ProposalField.OnlyIfPresent"/> is left out unless it has text.
    /// </summary>
    public static IReadOnlyList<(ProposalField Field, string Value)>? Read(string changeType, string json)
    {
        var fields = FieldsFor(changeType);
        if (fields.Count == 0 || Parse(json) is not { } payload)
        {
            return null;
        }

        var shown = new List<(ProposalField, string)>();
        foreach (var field in fields)
        {
            var text = Find(payload, field.Key) is { } key && payload[key] is JsonValue value && value.TryGetValue<string>(out var s)
                ? s
                : null;

            if (field.OnlyIfPresent && string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            shown.Add((field, text ?? string.Empty));
        }

        return shown;
    }

    /// <summary>
    /// The payload with the form's values written into it. Every property the form does not
    /// show is kept exactly as it was, in its place. Returns an error instead when a required
    /// field is empty; the API would refuse it too, but less clearly.
    /// </summary>
    public static (string? Json, string? Error) Write(string json, IEnumerable<(ProposalField Field, string Value)> values)
    {
        if (Parse(json) is not { } payload)
        {
            return (null, "This proposal can't be edited as a form. Use Advanced.");
        }

        foreach (var (field, raw) in values)
        {
            var value = raw.Trim();
            if (value.Length == 0)
            {
                if (field.Required)
                {
                    return (null, $"{field.Label} can't be empty.");
                }

                // Null, not "": every payload reads a missing or null field as "not given",
                // and an empty string as a value to store.
                if (Find(payload, field.Key) is { } existing)
                {
                    payload[existing] = null;
                }

                continue;
            }

            payload[Find(payload, field.Key) ?? field.Key] = value;
        }

        return (payload.ToJsonString(), null);
    }

    /// <summary>The JSON as the Advanced box shows it, or the text unchanged if it does not parse.</summary>
    public static string Prettify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The API writes camelCase and reads case-insensitively, so a payload edited by hand under
    // Advanced may hold "Name". The form writes to the property that is there, whatever its
    // casing, and never adds a second one beside it.
    private static string? Find(JsonObject payload, string key) =>
        payload.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
}
