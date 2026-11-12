using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nornis.Evals.Corpus;

/// <summary>
/// Writes the eval corpus into a folder: the demo world's notes, from the template the API
/// ships, and the Slime Spa chapters, from wherever Dave keeps them. The folder is the
/// machine's, never the repository's.
/// </summary>
public static partial class CorpusBuilder
{
    /// <summary>The demo world's text sources in the order a GM would have filed them: the
    /// gazetteer and the GM's prep first, then the sessions as played, then the sixth session
    /// the tutorial leaves as raw notes. The map and the merge note aren't prose to extract.</summary>
    public static EvalCorpus.ManifestWorld Demo(string templateZip, string folder)
    {
        using var zip = ZipFile.OpenRead(templateZip);
        using var sourcesJson = JsonDocument.Parse(Read(zip, "sources.json"));
        using var campaignsJson = JsonDocument.Parse(Read(zip, "campaigns.json"));
        var campaigns = campaignsJson.RootElement.GetProperty("campaigns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.GetProperty("name").GetString()!);

        var cases = new List<EvalCorpus.ManifestCase>();
        var sources = sourcesJson.RootElement.GetProperty("sources").EnumerateArray()
            .Where(s => s.GetProperty("type").GetString() is "SessionNote" or "GMNote")
            // A merge's record is titled "Entries merged" since 3.32; older ones still say "Artifact merge".
            .Where(s => s.GetProperty("title").GetString() is { } title
                && !title.StartsWith("Artifact merge", StringComparison.Ordinal)
                && !title.StartsWith("Entries merged", StringComparison.Ordinal))
            .OrderBy(s => s.GetProperty("type").GetString() == "SessionNote")
            .ThenBy(s => OccurredAt(s) ?? DateTimeOffset.MinValue)
            .ThenBy(s => s.GetProperty("title").GetString(), StringComparer.Ordinal);

        Directory.CreateDirectory(Path.Combine(folder, "demo"));
        foreach (var source in sources)
        {
            var title = source.GetProperty("title").GetString()!;
            var file = Path.Combine("demo", Slug(title) + ".md");
            File.WriteAllText(Path.Combine(folder, file), source.GetProperty("body").GetString());
            var campaign = source.TryGetProperty("campaignId", out var id) && id.GetString() is { } key && campaigns.TryGetValue(key, out var name)
                ? name
                : null;
            cases.Add(new EvalCorpus.ManifestCase(
                Slug(title), title, source.GetProperty("type").GetString()!, source.GetProperty("visibility").GetString()!,
                file.Replace('\\', '/'), OccurredAt(source), campaign));
        }

        var sixth = Read(zip, "tutorial/session-6.md");
        var sixthTitle = sixth.Split('\n')[0].TrimStart('#', ' ').Trim();
        File.WriteAllText(Path.Combine(folder, "demo", "session-6.md"), sixth);
        cases.Add(new EvalCorpus.ManifestCase(
            "session-6", sixthTitle, "SessionNote", "PartyVisible", "demo/session-6.md",
            new DateTimeOffset(2026, 7, 25, 19, 0, 0, TimeSpan.Zero), cases.LastOrDefault(c => c.Campaign is not null)?.Campaign));

        return new EvalCorpus.ManifestWorld("vespergale", "Vespergale Reach", cases);
    }

    /// <summary>The Slime Spa's chapters as the Prep export files them: GM-only adventure
    /// sources, read in chapter order. The files are named, not copied: they stay where Dave
    /// keeps them.</summary>
    public static EvalCorpus.ManifestWorld SlimeSpa(string chapterFolder) =>
        new("gribbits", "Gribbits Detective Agency", Directory.GetFiles(chapterFolder, "*.md")
            .Order(StringComparer.Ordinal)
            .Select(path => new EvalCorpus.ManifestCase(
                Path.GetFileNameWithoutExtension(path),
                ChapterTitle(path),
                "AdventureExport",
                "GMOnly",
                Path.GetFullPath(path)))
            .ToList());

    /// <summary>"Everybody Dies at the Slurping Spa: Gribbits Detective Agency Opening Sequence"
    /// from the chapter's first two headings.</summary>
    private static string ChapterTitle(string path)
    {
        var headings = File.ReadLines(path).Where(l => l.StartsWith('#')).Take(2)
            .Select(l => l.TrimStart('#', ' ').Trim()).ToList();
        return headings.Count == 0 ? Path.GetFileNameWithoutExtension(path) : string.Join(": ", headings);
    }

    private static DateTimeOffset? OccurredAt(JsonElement source) =>
        source.TryGetProperty("occurredAt", out var at) && at.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(at.GetString(), out var parsed) ? parsed : null;

    private static string Read(ZipArchive zip, string entry)
    {
        using var reader = new StreamReader((zip.GetEntry(entry) ?? throw new InvalidDataException($"The template has no {entry}.")).Open());
        return reader.ReadToEnd();
    }

    internal static string Slug(string title) => NotSlug().Replace(title.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}
