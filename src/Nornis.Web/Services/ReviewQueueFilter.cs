using System.Text.Json;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// Narrowing the review queue on screen. Two axes: the change type, which is the chip on every
/// card, and the visibility the proposal would give its target, which lives inside the proposed
/// value rather than on the proposal row. Both read from what is loaded — the API pages the
/// queue and the banner already says so — and null on either axis means "all".
/// </summary>
public static class ReviewQueueFilter
{
    /// <summary>Change types present in the queue, in the order they first appear.</summary>
    public static IReadOnlyList<string> Types(IEnumerable<ReviewProposal> proposals) =>
        proposals.Select(p => p.ChangeType).Distinct().ToList();

    /// <summary>
    /// Visibilities present in the queue, in the order pickers offer scopes. A proposal with
    /// no visibility of its own (a merge, a placemark) contributes nothing here and is hidden
    /// by any visibility filter.
    /// </summary>
    public static IReadOnlyList<string> Visibilities(IEnumerable<ReviewProposal> proposals)
    {
        var present = proposals.Select(p => Visibility(p.ProposedValueJson)).Where(v => v is not null).ToHashSet();
        return VisibilityDisplay.Scopes.Where(present.Contains).ToList();
    }

    public static IReadOnlyList<ReviewProposal> Apply(IEnumerable<ReviewProposal> proposals, string? type, string? visibility) =>
        proposals
            .Where(p => type is null || p.ChangeType == type)
            .Where(p => visibility is null || Visibility(p.ProposedValueJson) == visibility)
            .ToList();

    /// <summary>The visibility a proposal would set, or null when the payload carries none.</summary>
    public static string? Visibility(string json) => PayloadString(json, "visibility");

    /// <summary>
    /// One string property of a proposed value, by name, case-insensitively — the payloads are
    /// camelCase from the model and PascalCase from an edit, and neither should matter here.
    /// </summary>
    public static string? PayloadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
