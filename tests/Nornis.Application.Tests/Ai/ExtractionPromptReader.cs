using System.Globalization;
using System.Text.RegularExpressions;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// Reads the typed facts back out of a built extraction user message. The extraction
/// client receives finished prompt strings, so tests that used to assert on
/// ExtractionRequest fields now assert on what the model is actually shown — parsed
/// through this one reader, so drift in ExtractionPromptBuilder's rendering breaks
/// loudly here instead of silently across thirty string-contains assertions.
/// </summary>
public static class ExtractionPromptReader
{
    private const string SourceContentHeader = "## Source Content";
    private const string LocationContextHeader = "## Location Context (inferred from prior notes — not stated in this source)";
    private const string PublishedReferenceHeader = "## Published Reference (rulebooks and modules — not world canon)";
    private const string ExistingArtifactsHeader = "## Existing World Artifacts";

    private static readonly Regex ArtifactHeader = new(
        @"^### (?<name>.+) \(Id: (?<id>[0-9a-fA-F-]{36}), Type: (?<type>\w+)\)$", RegexOptions.Compiled);

    private static readonly Regex FactLine = new(
        @"^  - \[factId: (?<id>[0-9a-fA-F-]{36})\] (?<predicate>[^:]+): (?<value>.*)$", RegexOptions.Compiled);

    private static readonly Regex LocationLine = new(
        @"^- (?<name>.+) \(Id: (?<id>[0-9a-fA-F-]{36})\)(?: — (?<summary>.*))?$", RegexOptions.Compiled);

    private static readonly Regex LocationContextIntro = new(
        @"^As of ""(?<title>.+?)""(?: \((?<date>\d{4}-\d{2}-\d{2})\))?, the party was at:$", RegexOptions.Compiled);

    private static readonly Regex PassageLine = new(
        @"^- From ""(?<title>.+)"", p\. (?<page>\d+):$", RegexOptions.Compiled);

    public static ParsedExtractionPrompt Parse(string userMessage)
    {
        var lines = userMessage.Split('\n');
        var prompt = new ParsedExtractionPrompt();

        // The body is user text and may itself contain "## " lines (derived-text sections),
        // so sections are delimited by the builder's exact headers, never by prefix.
        var section = "## Source Information";
        var bodyLines = new List<string>();
        ParsedArtifact? artifact = null;
        ParsedLocationContext? locationContext = null;

        foreach (var line in lines)
        {
            if (line is SourceContentHeader or LocationContextHeader
                or PublishedReferenceHeader or ExistingArtifactsHeader)
            {
                section = line;
                continue;
            }

            switch (section)
            {
                case "## Source Information":
                    if (line.StartsWith("- Title: ", StringComparison.Ordinal))
                    {
                        prompt.SourceTitle = line["- Title: ".Length..];
                    }
                    else if (line.StartsWith("- Type: ", StringComparison.Ordinal))
                    {
                        prompt.SourceType = line["- Type: ".Length..];
                    }
                    else if (line.StartsWith("- Visibility: ", StringComparison.Ordinal))
                    {
                        prompt.SourceVisibility = line["- Visibility: ".Length..];
                    }
                    else if (line.StartsWith("- Occurred At: ", StringComparison.Ordinal))
                    {
                        prompt.OccurredAt = DateTimeOffset.Parse(
                            line["- Occurred At: ".Length..], CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith("- Campaign: ", StringComparison.Ordinal))
                    {
                        var campaign = line["- Campaign: ".Length..];
                        var dashAt = campaign.IndexOf(" — ", StringComparison.Ordinal);
                        if (dashAt >= 0)
                        {
                            campaign = campaign[..dashAt];
                        }

                        var statusMatch = Regex.Match(campaign, @"^(?<name>.*) \((?<status>\w+)\)$");
                        prompt.CampaignName = statusMatch.Success ? statusMatch.Groups["name"].Value : campaign;
                        prompt.CampaignStatus = statusMatch.Success ? statusMatch.Groups["status"].Value : null;
                    }

                    break;

                case LocationContextHeader:
                    if (LocationContextIntro.Match(line) is { Success: true } intro)
                    {
                        locationContext = new ParsedLocationContext
                        {
                            SourceTitle = intro.Groups["title"].Value,
                            OccurredAtDate = intro.Groups["date"].Success
                                ? DateOnly.ParseExact(intro.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                                : null
                        };
                        prompt.RecentLocations = locationContext;
                    }
                    else if (locationContext is not null && LocationLine.Match(line) is { Success: true } loc)
                    {
                        locationContext.Locations.Add(new ParsedLocation
                        {
                            Id = Guid.Parse(loc.Groups["id"].Value),
                            Name = loc.Groups["name"].Value,
                            Summary = loc.Groups["summary"].Success ? loc.Groups["summary"].Value : null
                        });
                    }

                    break;

                case SourceContentHeader:
                    bodyLines.Add(line);
                    break;

                case PublishedReferenceHeader:
                    if (PassageLine.Match(line) is { Success: true } passage)
                    {
                        prompt.ReferencePassages.Add(new ParsedPassage
                        {
                            DocumentTitle = passage.Groups["title"].Value,
                            Page = int.Parse(passage.Groups["page"].Value, CultureInfo.InvariantCulture)
                        });
                    }

                    break;

                case ExistingArtifactsHeader:
                    if (ArtifactHeader.Match(line) is { Success: true } header)
                    {
                        artifact = new ParsedArtifact
                        {
                            Id = Guid.Parse(header.Groups["id"].Value),
                            Name = header.Groups["name"].Value,
                            Type = header.Groups["type"].Value
                        };
                        prompt.ExistingArtifacts.Add(artifact);
                    }
                    else if (artifact is not null)
                    {
                        if (line.StartsWith("Summary: ", StringComparison.Ordinal))
                        {
                            artifact.Summary = line["Summary: ".Length..];
                        }
                        else if (line.StartsWith("Part of: ", StringComparison.Ordinal))
                        {
                            artifact.PartOfName = line["Part of: ".Length..];
                        }
                        else if (FactLine.Match(line) is { Success: true } fact)
                        {
                            artifact.Facts.Add(new ParsedFact
                            {
                                Id = Guid.Parse(fact.Groups["id"].Value),
                                Predicate = fact.Groups["predicate"].Value,
                                Value = fact.Groups["value"].Value
                            });
                        }
                    }

                    break;
            }
        }

        // The builder joins sections with a blank separator line; that separator is not body.
        prompt.SourceBody = string.Join('\n', bodyLines).Trim('\n');
        return prompt;
    }
}

public sealed class ParsedExtractionPrompt
{
    public string? SourceTitle { get; set; }
    public string? SourceType { get; set; }
    public string? SourceVisibility { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
    public string? CampaignName { get; set; }
    public string? CampaignStatus { get; set; }
    public string SourceBody { get; set; } = string.Empty;
    public List<ParsedPassage> ReferencePassages { get; } = [];
    public ParsedLocationContext? RecentLocations { get; set; }
    public List<ParsedArtifact> ExistingArtifacts { get; } = [];
}

public sealed class ParsedArtifact
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Summary { get; set; }
    public string? PartOfName { get; set; }
    public List<ParsedFact> Facts { get; } = [];
}

public sealed class ParsedFact
{
    public required Guid Id { get; init; }
    public required string Predicate { get; init; }
    public required string Value { get; init; }
}

public sealed class ParsedPassage
{
    public required string DocumentTitle { get; init; }
    public required int Page { get; init; }
}

public sealed class ParsedLocationContext
{
    public required string SourceTitle { get; init; }
    public DateOnly? OccurredAtDate { get; init; }
    public List<ParsedLocation> Locations { get; } = [];
}

public sealed class ParsedLocation
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Summary { get; set; }
}
