using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nornis.Evals.Corpus;
using Nornis.Evals.Running;

namespace Nornis.Evals.Scoring;

/// <summary>
/// The reviewed answer every model is scored against: one model's run over the corpus
/// (<c>baseline.json</c>) and, per world, a Markdown checklist of its proposals that Dave
/// reviews once (<c>{world}.md</c>). A ticked line is a proposal he would have accepted in
/// Review; an unticked one is dropped. The ticks are read back on every run, so correcting a
/// line later rescores everything.
/// </summary>
public static partial class Baseline
{
    public const string RunFile = "baseline.json";

    public static void Write(string folder, EvalCorpus corpus, RunRecord run)
    {
        Directory.CreateDirectory(folder);
        run.Save(Path.Combine(folder, RunFile));

        var replay = WorldReplay.From(KeptByWorld(corpus, run, kept: null));
        foreach (var world in corpus.Worlds)
        {
            var names = replay.Names(world.Id);
            var text = new StringBuilder()
                .AppendLine($"# Baseline: {world.Name}")
                .AppendLine()
                .AppendLine($"Read by {run.Model} on {run.RanAt:yyyy-MM-dd}, each source against the world as the sources before it left it.")
                .AppendLine("Untick a proposal you would have rejected in Review and leave the rest ticked. Every model is")
                .AppendLine("scored against what stays ticked, and later sources are read against it too. Nothing else on")
                .AppendLine("a line matters, so a note after it is fine.")
                .AppendLine();

            foreach (var evalCase in world.Cases)
            {
                text.AppendLine($"## {evalCase.Title}").AppendLine();
                var record = run.Cases.FirstOrDefault(c => c.World == world.Id && c.Case == evalCase.Id && c.Pass == 1);
                if (record?.Proposals is not { } proposals)
                {
                    text.AppendLine($"_No valid answer: {string.Join("; ", record?.Failures ?? ["not read"])}_").AppendLine();
                    continue;
                }

                if (proposals.Count == 0)
                {
                    text.AppendLine("_Nothing proposed._");
                }

                for (var i = 0; i < proposals.Count; i++)
                {
                    text.AppendLine($"- [x] `{evalCase.Id}#{i + 1}` {ProposalView.Read(proposals[i], names).Describe()}");
                }

                text.AppendLine();
            }

            File.WriteAllText(Path.Combine(folder, $"{world.Id}.md"), text.ToString());
        }
    }

    /// <summary>The kept proposals, per world, one list per case in corpus order. A case the
    /// baseline couldn't read contributes nothing.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<JsonElement>>> Load(string folder, EvalCorpus corpus)
    {
        var run = RunRecord.Load(Path.Combine(folder, RunFile));
        var ticks = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var world in corpus.Worlds)
        {
            var checklist = Path.Combine(folder, $"{world.Id}.md");
            if (!File.Exists(checklist))
            {
                throw new FileNotFoundException($"The baseline has no checklist for {world.Id} ({checklist}).");
            }

            foreach (var (id, kept) in ReadTicks(File.ReadAllText(checklist)))
            {
                ticks[id] = kept;
            }
        }

        return KeptByWorld(corpus, run, ticks);
    }

    /// <summary>Each checklist line's id and whether it is ticked.</summary>
    internal static IEnumerable<(string Id, bool Kept)> ReadTicks(string checklist) =>
        Tick().Matches(checklist).Select(m => (m.Groups["id"].Value, m.Groups["mark"].Value is "x" or "X"));

    /// <param name="kept">Null keeps every proposal: the checklist as first written.</param>
    private static Dictionary<string, IReadOnlyList<IReadOnlyList<JsonElement>>> KeptByWorld(
        EvalCorpus corpus, RunRecord run, IReadOnlyDictionary<string, bool>? kept) =>
        corpus.Worlds.ToDictionary(
            w => w.Id,
            w => (IReadOnlyList<IReadOnlyList<JsonElement>>)w.Cases.Select(c =>
            {
                var proposals = run.Cases.FirstOrDefault(r => r.World == w.Id && r.Case == c.Id && r.Pass == 1)?.Proposals ?? [];

                // A line missing from the checklist (deleted rather than unticked) is dropped.
                return (IReadOnlyList<JsonElement>)proposals
                    .Where((_, i) => kept is null || kept.GetValueOrDefault($"{c.Id}#{i + 1}"))
                    .ToList();
            }).ToList(),
            StringComparer.Ordinal);

    [GeneratedRegex(@"^\s*[-*]\s+\[(?<mark>[ xX])\]\s+`(?<id>[^`#]+#\d+)`", RegexOptions.Multiline)]
    private static partial Regex Tick();
}
