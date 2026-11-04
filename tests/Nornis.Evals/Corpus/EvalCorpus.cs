using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nornis.Evals.Corpus;

/// <summary>
/// The eval's fixed corpus: worlds, each an ordered run of sources read as Nornis would read
/// them. The manifest (<c>corpus.json</c>) names each source's file; the text itself is never
/// committed (the Slime Spa chapters are Dave's), so a corpus is a folder on the machine that
/// runs the eval.
/// </summary>
public sealed record EvalCorpus(IReadOnlyList<EvalWorld> Worlds)
{
    public const string ManifestName = "corpus.json";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads a manifest and every file it names, relative to the manifest's folder
    /// unless a path is absolute. A missing file is an error, not a skipped case: a corpus
    /// that silently shrinks would make two reports look comparable when they aren't.</summary>
    public static EvalCorpus Load(string manifestPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Json)
            ?? throw new InvalidDataException($"{manifestPath} is empty.");

        var worlds = manifest.Worlds.Select(world => new EvalWorld(
            world.Id,
            world.Name,
            world.Cases.Select(c => new EvalCase(
                c.Id,
                c.Title,
                c.Type,
                c.Visibility,
                c.OccurredAt,
                c.Campaign,
                File.ReadAllText(Path.IsPathRooted(c.File) ? c.File : Path.Combine(folder, c.File)))).ToList())).ToList();

        var duplicate = worlds.SelectMany(w => w.Cases.Select(c => $"{w.Id}/{c.Id}"))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null
            ? new EvalCorpus(worlds)
            : throw new InvalidDataException($"{manifestPath} names the case {duplicate.Key} twice.");
    }

    public static void Save(string manifestPath, Manifest manifest) =>
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Json));

    public sealed record Manifest(IReadOnlyList<ManifestWorld> Worlds);

    public sealed record ManifestWorld(string Id, string Name, IReadOnlyList<ManifestCase> Cases);

    /// <param name="Type">A <c>SourceType</c> name, as the prompt shows it.</param>
    /// <param name="Visibility">A <c>Visibility</c> name: GM-only sources are read differently.</param>
    public sealed record ManifestCase(
        string Id,
        string Title,
        string Type,
        string Visibility,
        string File,
        DateTimeOffset? OccurredAt = null,
        string? Campaign = null);
}

/// <summary>One world's sources, in the order they are read; each one's context is the world as
/// the ones before it left it.</summary>
public sealed record EvalWorld(string Id, string Name, IReadOnlyList<EvalCase> Cases);

public sealed record EvalCase(
    string Id,
    string Title,
    string Type,
    string Visibility,
    DateTimeOffset? OccurredAt,
    string? Campaign,
    string Body);
