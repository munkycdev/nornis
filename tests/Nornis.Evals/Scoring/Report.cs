using System.Globalization;
using System.Text;
using System.Text.Json;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Evals.Corpus;
using Nornis.Evals.Running;

namespace Nornis.Evals.Scoring;

/// <summary>
/// One model's run scored against the reviewed baseline, as Markdown: totals, a line per case,
/// then for each case what was missed, what was invented and where matched proposals say
/// different things. "Invented" means "not in the baseline", which is not always wrong: the
/// baseline is one model's reading, so the list is there to be read, not only counted.
/// </summary>
public static class Report
{
    public static string Write(
        EvalCorpus corpus, RunRecord run, IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<JsonElement>>> kept, AiModelPrice? price)
    {
        var replay = WorldReplay.From(kept);
        var rows = new List<Row>();
        foreach (var world in corpus.Worlds)
        {
            var names = replay.Names(world.Id);
            for (var i = 0; i < world.Cases.Count; i++)
            {
                var expected = kept[world.Id][i].Select(p => ProposalView.Read(p, names)).ToList();
                foreach (var record in run.Cases.Where(c => c.World == world.Id && c.Case == world.Cases[i].Id).OrderBy(c => c.Pass))
                {
                    var answer = (record.Proposals ?? []).Select(p => ProposalView.Read(p, names)).ToList();
                    rows.Add(new Row(world, world.Cases[i], record, record.Proposals is null ? null : Scorer.Score(expected, answer), expected.Count));
                }
            }
        }

        var text = new StringBuilder()
            .AppendLine($"# Extraction eval: {run.Model}")
            .AppendLine()
            .AppendLine($"Run {run.RanAt:yyyy-MM-dd HH:mm} UTC over {corpus.Worlds.Sum(w => w.Cases.Count)} sources"
                        + (run.Repeat > 1 ? $", each read {run.Repeat} times" : "") + ", scored against the reviewed baseline.")
            .AppendLine("Every source was read against the same world (the baseline's), so the numbers differ only by the model.")
            .AppendLine();

        Totals(text, rows, price);
        Cases(text, rows, price);
        foreach (var row in rows.Where(r => r.Score is null || r.Score.Missed.Count + r.Score.Invented.Count > 0 || r.Score.Differences.Any()))
        {
            Details(text, row, run.Repeat);
        }

        return text.ToString();
    }

    private static void Totals(StringBuilder text, List<Row> rows, AiModelPrice? price)
    {
        var scored = rows.Where(r => r.Score is not null).ToList();
        var found = scored.Sum(r => r.Score!.Found.Count);
        var missed = scored.Sum(r => r.Score!.Missed.Count);
        var invented = scored.Sum(r => r.Score!.Invented.Count);
        var usage = rows.SelectMany(r => r.Record.Usage).ToList();

        text.AppendLine("| | |")
            .AppendLine("|---|---|")
            .AppendLine($"| Found | {found} of {found + missed} ({Percent(found, found + missed)}) |")
            .AppendLine($"| Missed | {missed} |")
            .AppendLine($"| Invented (not in the baseline) | {invented} ({Percent(invented, found + invented)} of what it proposed) |")
            .AppendLine($"| Sources with no valid answer | {rows.Count(r => r.Score is null)} of {rows.Count} |")
            .AppendLine($"| Answers refused (schema or validation), retried | {rows.Sum(r => r.Record.Failures.Count(f => !f.StartsWith("HTTP", StringComparison.Ordinal)))} |")
            .AppendLine($"| Tokens in / out | {usage.Sum(u => u.InputTokens):N0} / {usage.Sum(u => u.OutputTokens):N0} |")
            .AppendLine($"| Cost | {Cost(usage, price)} |")
            .AppendLine($"| Time per source (median) | {Median(rows.Select(r => r.Record.Usage.Sum(u => u.DurationMs)))} |")
            .AppendLine();
    }

    private static void Cases(StringBuilder text, List<Row> rows, AiModelPrice? price)
    {
        text.AppendLine("| Source | Found | Missed | Invented | Refused | Cost |")
            .AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var row in rows)
        {
            var title = row.Record.Pass > 1 ? $"{row.Case.Title} (pass {row.Record.Pass})" : row.Case.Title;
            text.AppendLine(row.Score is { } score
                ? $"| {title} | {score.Found.Count} of {row.Expected} | {score.Missed.Count} | {score.Invented.Count} | {row.Record.Failures.Count} | {Cost(row.Record.Usage, price)} |"
                : $"| {title} | no valid answer | | | {row.Record.Failures.Count} | {Cost(row.Record.Usage, price)} |");
        }

        text.AppendLine();
    }

    private static void Details(StringBuilder text, Row row, int repeat)
    {
        text.AppendLine($"## {row.Case.Title}{(repeat > 1 ? $" (pass {row.Record.Pass})" : "")}").AppendLine();
        if (row.Record.Failures.Count > 0)
        {
            text.AppendLine("Refused answers:");
            foreach (var failure in row.Record.Failures)
            {
                text.AppendLine($"- {failure}");
            }

            text.AppendLine();
        }

        if (row.Score is not { } score)
        {
            return;
        }

        List("Missed", score.Missed, text);
        List("Invented", score.Invented, text);
        var differences = score.Differences.ToList();
        if (differences.Count > 0)
        {
            text.AppendLine("Matched, but worded differently:").AppendLine();
            foreach (var (baseline, answer) in differences)
            {
                text.AppendLine($"- Baseline: {baseline.Describe()}").AppendLine($"  - This model: {answer.Describe()}");
            }

            text.AppendLine();
        }
    }

    private static void List(string heading, IReadOnlyList<ProposalView> proposals, StringBuilder text)
    {
        if (proposals.Count == 0)
        {
            return;
        }

        text.AppendLine($"{heading}:").AppendLine();
        foreach (var proposal in proposals)
        {
            text.AppendLine($"- {proposal.Describe()}");
        }

        text.AppendLine();
    }

    /// <summary>Every attempt is paid for, refused ones included, as the ledger records them.</summary>
    private static string Cost(IEnumerable<AiUsage> usage, AiModelPrice? price) =>
        price is null
            ? "unknown (no price)"
            : "$" + usage.Sum(u => price.Cost(u.InputTokens, u.CachedInputTokens, u.OutputTokens)).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Percent(int part, int whole) =>
        whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Median(IEnumerable<int> milliseconds)
    {
        var sorted = milliseconds.Order().ToList();
        return sorted.Count == 0 ? "n/a" : $"{sorted[sorted.Count / 2] / 1000.0:0.0}s";
    }

    private sealed record Row(EvalWorld World, EvalCase Case, CaseRecord Record, CaseScore? Score, int Expected);
}
