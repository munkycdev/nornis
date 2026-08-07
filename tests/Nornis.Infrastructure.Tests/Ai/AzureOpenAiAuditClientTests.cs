using System.Text.Json;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The audit's finding categories are declared in two places that no compiler spans: the
/// domain enum, and the strict JSON schema this client sends. Strict mode means the schema
/// is the real gate — a category missing from it cannot be emitted at all, and the failure
/// is silent: the model simply never reports that kind of problem, and no error is raised
/// anywhere. That is exactly the shape of bug the comment rule calls out ("mirrors X" is
/// legitimate only when no compiler can enforce the sameness), so the enforcement lives
/// here instead of in a comment.
/// </summary>
[TestFixture]
public class AzureOpenAiAuditClientTests
{
    /// <summary>Pulls a string-enum array out of the schema by property name.</summary>
    private static IReadOnlyList<string> SchemaEnumValues(string propertyName)
    {
        using var document = JsonDocument.Parse(AzureOpenAiAuditClient.GetStructuredOutputSchema());

        var property = document.RootElement
            .GetProperty("properties").GetProperty("findings")
            .GetProperty("items").GetProperty("properties")
            .GetProperty(propertyName);

        return property.GetProperty("enum")
            .EnumerateArray()
            .Select(v => v.GetString()!)
            .ToList();
    }

    [Test]
    public void SchemaCategories_MatchTheDomainEnum_Exactly()
    {
        // Not "contains" — equivalence in both directions. A schema value the enum lost would
        // let the model emit a category the service then drops on the floor, and an enum
        // member the schema lacks is a category that can never be reported.
        Assert.That(
            SchemaEnumValues("category"),
            Is.EquivalentTo(Enum.GetNames<ContinuityFindingCategory>()),
            "the audit schema's categories and ContinuityFindingCategory must name the same set — "
            + "strict structured output means the schema silently decides what the model may report");
    }

    [Test]
    public void SchemaSeverities_MatchTheDomainEnum_Exactly()
    {
        Assert.That(
            SchemaEnumValues("severity"),
            Is.EquivalentTo(Enum.GetNames<ContinuityFindingSeverity>()),
            "severity drives the continuity penalty, so a mismatch here silently changes the score");
    }
}
