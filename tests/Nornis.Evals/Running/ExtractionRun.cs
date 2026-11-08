using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Application.Services;
using Nornis.Evals.Corpus;
using Nornis.Evals.Models;
using Nornis.Infrastructure.Ai;

namespace Nornis.Evals.Running;

/// <summary>What the world already knows when a case is read: the eval's stand-in for the
/// context <c>ExtractionService</c> assembles from the database.</summary>
public interface IEvalContext
{
    IReadOnlyList<ArtifactContext> Before(EvalWorld world, int caseIndex);

    /// <summary>What a case's first pass answered; null when no attempt produced a valid answer.</summary>
    void Read(EvalWorld world, int caseIndex, IReadOnlyList<JsonElement>? proposals);
}

/// <summary>A world that knows nothing yet, for a run with no baseline to build context from.</summary>
public sealed class EmptyContext : IEvalContext
{
    public static readonly EmptyContext Instance = new();

    public IReadOnlyList<ArtifactContext> Before(EvalWorld world, int caseIndex) => [];

    public void Read(EvalWorld world, int caseIndex, IReadOnlyList<JsonElement>? proposals)
    {
    }
}

/// <summary>
/// Reads every case of a corpus with one model, through the real prompt builder and the real
/// extraction client, with the Worker's own retry rule: an answer that fails to parse or to
/// validate is asked for again, up to <c>Extraction:MaxParseRetryAttempts</c> more times.
/// </summary>
public sealed class ExtractionRunner(IAiCallBinder binder, ILoggerFactory logging, int maxParseRetries = 2, int timeoutSeconds = 300)
{
    private readonly ExtractionAiClient _client = new(binder, logging.CreateLogger<ExtractionAiClient>());

    public async Task<RunRecord> RunAsync(EvalCorpus corpus, EvalModel model, IEvalContext context, int repeat, CancellationToken ct)
    {
        var cases = new List<CaseRecord>();
        foreach (var world in corpus.Worlds)
        {
            for (var index = 0; index < world.Cases.Count; index++)
            {
                for (var pass = 1; pass <= repeat; pass++)
                {
                    var evalCase = world.Cases[index];
                    Console.WriteLine($"  {world.Id}/{evalCase.Id}{(repeat > 1 ? $" (pass {pass})" : "")}");
                    var read = await ReadAsync(world, index, context.Before(world, index), pass, ct);
                    cases.Add(read);
                    if (pass == 1)
                    {
                        context.Read(world, index, read.Proposals);
                    }
                }
            }
        }

        return new RunRecord(model.Name, DateTimeOffset.UtcNow, repeat, cases);
    }

    private async Task<CaseRecord> ReadAsync(EvalWorld world, int index, IReadOnlyList<ArtifactContext> known, int pass, CancellationToken ct)
    {
        var evalCase = world.Cases[index];
        var extraction = new ExtractionRequest
        {
            SourceBody = evalCase.Body,
            SourceTitle = evalCase.Title,
            SourceType = evalCase.Type,
            SourceVisibility = evalCase.Visibility,
            OccurredAt = evalCase.OccurredAt,
            CampaignName = evalCase.Campaign,
            CampaignStatus = evalCase.Campaign is null ? null : "Active",
            ExistingArtifacts = known,
        };
        var request = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(Guid.Empty),
            SystemPrompt = ExtractionPromptBuilder.BuildSystemPrompt(extraction),
            UserMessage = ExtractionPromptBuilder.BuildUserMessage(extraction),
            Model = "eval",
            TimeoutSeconds = timeoutSeconds,
        };

        var failures = new List<string>();
        var usages = new List<AiUsage>();
        for (var attempt = 1; attempt <= 1 + maxParseRetries; attempt++)
        {
            try
            {
                var response = await _client.ExtractAsync(request, ct);
                usages.Add(response.Usage);
                if (ExtractionService.ValidateResponse(response) is { } invalid)
                {
                    failures.Add(invalid);
                    continue;
                }

                // The Worker drops Incidental proposals before filing (feature 43); the eval
                // counts what would have reached the queue, not what the model typed.
                var kept = ExtractionTriage.Apply(response.Proposals).Kept;
                return new CaseRecord(world.Id, evalCase.Id, pass, known.Count, Proposals(kept), failures, usages);
            }
            catch (AiParseException ex)
            {
                failures.Add(ex.Message);
                if (ex.Usage is { } usage)
                {
                    usages.Add(usage);
                }
            }
            catch (AiHttpException ex)
            {
                // A provider failure isn't the model's answer: record it and move on, so one
                // overloaded minute doesn't throw away the rest of the run.
                failures.Add($"HTTP {ex.StatusCode?.ToString() ?? "(unreachable)"}: {ex.Message}");
                break;
            }
            catch (AiTimeoutException ex)
            {
                failures.Add(ex.Message);
                break;
            }
        }

        return new CaseRecord(world.Id, evalCase.Id, pass, known.Count, null, failures, usages);
    }

    private static List<JsonElement> Proposals(IEnumerable<ExtractionProposal> proposals) =>
        proposals.Select(p => JsonSerializer.SerializeToElement(p, EvalCorpus.Json)).ToList();
}

/// <summary>One model's pass over the corpus, written as JSON beside the report so a report can
/// be rebuilt, or rescored against a corrected baseline, without paying for the calls again.</summary>
public sealed record RunRecord(string Model, DateTimeOffset RanAt, int Repeat, IReadOnlyList<CaseRecord> Cases)
{
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, EvalCorpus.Json));

    public static RunRecord Load(string path) =>
        JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), EvalCorpus.Json)
        ?? throw new InvalidDataException($"{path} is empty.");
}

/// <param name="Proposals">Null when no attempt produced a valid answer.</param>
/// <param name="KnownArtifacts">How many artifacts the context gave the model.</param>
/// <param name="Failures">Every attempt that was refused, in order, including ones a retry recovered.</param>
public sealed record CaseRecord(
    string World,
    string Case,
    int Pass,
    int KnownArtifacts,
    IReadOnlyList<JsonElement>? Proposals,
    IReadOnlyList<string> Failures,
    IReadOnlyList<AiUsage> Usage);
