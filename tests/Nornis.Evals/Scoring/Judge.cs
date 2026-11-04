using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Evals.Corpus;
using Nornis.Infrastructure.Ai;

namespace Nornis.Evals.Scoring;

/// <summary>
/// The second opinion on a scored case (Dave, 2026-11-04: "Add an AI judge too"). The key
/// matching runs first and stays the score of record; the judge rereads the source and rules
/// only on what the matching couldn't settle: whether a missed reference proposal is in fact
/// covered by a differently worded candidate, whether each candidate left over is supported by
/// the source, and whether a matched pair worded differently says the same thing. It sees the
/// source and both sides' proposals, never which model wrote them, and one judge model should
/// score every run so the numbers stay comparable. A case with nothing to settle costs nothing.
/// </summary>
public sealed class Judge(IAiCallBinder binder, string model, ILogger logger, int timeoutSeconds = 300)
{
    public const string Supported = "supported";
    public const string Unsupported = "unsupported";
    public const string Redundant = "redundant";
    public const string Contradicts = "contradicts";

    internal const string SystemPrompt = """
        You check a knowledge-extraction model's proposals for a tabletop role-playing campaign
        record. You are given one source text, the reference proposals a human reviewer accepted
        for it (R1, R2, ...), the candidate's proposals that an automatic matcher could not pair
        with any reference (C1, C2, ...), and pairs the matcher did pair but whose wording
        differs (P1, P2, ...).

        Rule only on the source text. Do not reward length or detail for its own sake.

        1. recovered: for each reference R that some candidate C records as the same change (the
           same artifact and the same information, however worded), give the pair. Each R and
           each C appears at most once. Leave out references nothing covers.
        2. candidates: for every C you did not use in recovered, one verdict:
           - "supported": the source states or plainly implies it; a correct proposal the
             reference lacks.
           - "unsupported": not in the source, wrong, or contradicting it.
           - "redundant": repeats another proposal, reference or candidate.
           Give a reason of one short sentence.
        3. pairs: for each P, "same" (says the same thing), "compatible" (narrower or broader,
           not wrong) or "contradicts" (the candidate's side conflicts with the reference or the
           source).
        """;

    internal static string Schema => """
        {
          "type": "object",
          "properties": {
            "recovered": { "type": "array", "items": { "type": "object",
              "properties": { "reference": { "type": "string" }, "candidate": { "type": "string" } },
              "required": ["reference", "candidate"], "additionalProperties": false } },
            "candidates": { "type": "array", "items": { "type": "object",
              "properties": { "candidate": { "type": "string" },
                              "verdict": { "type": "string", "enum": ["supported", "unsupported", "redundant"] },
                              "reason": { "type": "string" } },
              "required": ["candidate", "verdict", "reason"], "additionalProperties": false } },
            "pairs": { "type": "array", "items": { "type": "object",
              "properties": { "pair": { "type": "string" },
                              "verdict": { "type": "string", "enum": ["same", "compatible", "contradicts"] } },
              "required": ["pair", "verdict"], "additionalProperties": false } }
          },
          "required": ["recovered", "candidates", "pairs"],
          "additionalProperties": false
        }
        """;

    public bool NeedsJudging(CaseScore score) => score.Missed.Count + score.Invented.Count > 0 || score.Differences.Any();

    public async Task<CaseJudgement> JudgeAsync(string world, EvalCase evalCase, int pass, CaseScore score, IReadOnlyList<ProposalView> reference, CancellationToken ct)
    {
        var differences = score.Differences.ToList();
        var request = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(Guid.Empty),
            SystemPrompt = SystemPrompt,
            UserMessage = UserMessage(evalCase, reference, score.Invented, differences),
            Model = model,
            TimeoutSeconds = timeoutSeconds,
        };

        try
        {
            var (content, usage) = await AiCallExecutor.ExecuteAsync(
                binder, AiTask.Extraction, request, new AiCallOptions(new AiJsonSchema("eval_judgement", Schema)), "Eval judge", logger, ct);
            return Parse(world, evalCase.Id, pass, content, reference, score, differences) with { Usage = [usage] };
        }
        catch (Exception ex) when (ex is AiHttpException or AiTimeoutException or JsonException or InvalidOperationException)
        {
            return new CaseJudgement(world, evalCase.Id, pass, [], [], [], [], ex.Message);
        }
    }

    /// <summary>The whole reference goes in, not only what was missed, so the judge can tell a
    /// candidate that repeats a matched reference ("redundant") from a new one.</summary>
    internal static string UserMessage(EvalCase evalCase, IReadOnlyList<ProposalView> reference, IReadOnlyList<ProposalView> unpaired,
        IReadOnlyList<(ProposalView Baseline, ProposalView Answer)> differences)
    {
        var text = new StringBuilder()
            .AppendLine($"## Source: {evalCase.Title} ({evalCase.Type}, {evalCase.Visibility})")
            .AppendLine(evalCase.Body)
            .AppendLine()
            .AppendLine("## Reference proposals");
        for (var i = 0; i < reference.Count; i++)
        {
            text.AppendLine($"R{i + 1}. {reference[i].Describe()}");
        }

        text.AppendLine().AppendLine("## Unpaired candidate proposals");
        for (var i = 0; i < unpaired.Count; i++)
        {
            text.AppendLine($"C{i + 1}. {unpaired[i].Describe()}");
        }

        text.AppendLine().AppendLine("## Paired, worded differently");
        for (var i = 0; i < differences.Count; i++)
        {
            text.AppendLine($"P{i + 1}. Reference: {differences[i].Baseline.Describe()}")
                .AppendLine($"    Candidate: {differences[i].Answer.Describe()}");
        }

        return text.ToString();
    }

    /// <summary>Reads the verdicts back by the lines they name, so a judgement stays attached to
    /// the right proposals however the lists are later ordered. An id the judge made up, a
    /// reference already matched, or a second use of one candidate is ignored.</summary>
    internal static CaseJudgement Parse(string world, string caseId, int pass, string content, IReadOnlyList<ProposalView> reference,
        CaseScore score, IReadOnlyList<(ProposalView Baseline, ProposalView Answer)> differences)
    {
        var root = JsonNode.Parse(content)?.AsObject() ?? throw new JsonException("The judge answered nothing.");
        var missed = score.Missed.Select(m => m.Describe()).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<int>();
        var recovered = new List<JudgedMatch>();
        foreach (var item in root["recovered"]?.AsArray() ?? [])
        {
            if (Index(item?["reference"], 'R', reference.Count) is { } r && Index(item?["candidate"], 'C', score.Invented.Count) is { } c
                && missed.Remove(reference[r].Describe()) && used.Add(c))
            {
                recovered.Add(new JudgedMatch(reference[r].Describe(), score.Invented[c].Describe()));
            }
        }

        var candidates = new List<JudgedCandidate>();
        foreach (var item in root["candidates"]?.AsArray() ?? [])
        {
            if (Index(item?["candidate"], 'C', score.Invented.Count) is { } c && used.Add(c))
            {
                candidates.Add(new JudgedCandidate(score.Invented[c].Describe(),
                    item?["verdict"]?.GetValue<string>() ?? Unsupported, item?["reason"]?.GetValue<string>() ?? ""));
            }
        }

        var pairs = new List<JudgedPair>();
        foreach (var item in root["pairs"]?.AsArray() ?? [])
        {
            if (Index(item?["pair"], 'P', differences.Count) is { } p)
            {
                pairs.Add(new JudgedPair(differences[p].Baseline.Describe(), differences[p].Answer.Describe(), item?["verdict"]?.GetValue<string>() ?? "same"));
            }
        }

        return new CaseJudgement(world, caseId, pass, recovered, candidates, pairs, [], null);
    }

    private static int? Index(JsonNode? node, char prefix, int count) =>
        node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>() is { Length: > 1 } id && id[0] == prefix
        && int.TryParse(id.AsSpan(1), out var n) && n >= 1 && n <= count ? n - 1 : null;
}

/// <summary>A judge's verdicts on one run, kept beside it (<c>{run}.judge.json</c>) so the
/// report can be rebuilt without asking again.</summary>
public sealed record JudgeRecord(string Judge, DateTimeOffset JudgedAt, IReadOnlyList<CaseJudgement> Cases)
{
    public static string PathFor(string runPath) => Path.ChangeExtension(runPath, ".judge.json");

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, EvalCorpus.Json));

    public static JudgeRecord? LoadIfAny(string runPath) =>
        File.Exists(PathFor(runPath)) ? JsonSerializer.Deserialize<JudgeRecord>(File.ReadAllText(PathFor(runPath)), EvalCorpus.Json) : null;

    public CaseJudgement? For(string world, string caseId, int pass) =>
        Cases.FirstOrDefault(c => c.World == world && c.Case == caseId && c.Pass == pass);
}

/// <param name="Failure">Why the judge gave no verdict, when it didn't.</param>
public sealed record CaseJudgement(
    string World,
    string Case,
    int Pass,
    IReadOnlyList<JudgedMatch> Recovered,
    IReadOnlyList<JudgedCandidate> Candidates,
    IReadOnlyList<JudgedPair> Pairs,
    IReadOnlyList<AiUsage> Usage,
    string? Failure);

/// <summary>Proposals are named by the line a reviewer reads (<see cref="ProposalView.Describe"/>).</summary>
public sealed record JudgedMatch(string Reference, string Candidate);

public sealed record JudgedCandidate(string Candidate, string Verdict, string Reason);

public sealed record JudgedPair(string Reference, string Candidate, string Verdict);
