using System.Text;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IConvergenceNarrationService
{
    /// <summary>
    /// The gauge with a sentence of timing beside its top candidates. Every failure mode —
    /// no AI configured, budget spent, the call itself — returns the mechanical ranking
    /// unannotated rather than an error: the ranking is the feature, and this decorates it.
    /// </summary>
    Task<AppResult<ConvergenceGauge>> NarrateAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}

public class ConvergenceNarrationService : IConvergenceNarrationService
{
    /// <summary>
    /// How many candidates the model is asked about. The gauge shows fifty; narrating all of
    /// them would spend on rows a GM will never scroll to, and the sentence earns its cost
    /// only near the top.
    /// </summary>
    public const int MaxNarrated = 8;

    /// <summary>
    /// The prompt lives here, not in the adapter — Application owns what is said, the adapter
    /// owns how it travels. What it may not do is the load-bearing half: the ranking is already
    /// decided, and a model that re-argues the order produces a page whose numbers and
    /// sentences disagree.
    /// </summary>
    internal const string SystemPrompt = """
        You are helping a tabletop GM decide which of their hidden secrets to reveal to the
        players next. The system has already ranked them. Your job is ONLY to write one short
        sentence per candidate saying why this is a good moment — the dramatic timing, which is
        the one thing the ranking's arithmetic cannot see.

        Rules:
        - Do NOT re-rank, re-score, or argue that a candidate should be higher or lower. The
          order is settled.
        - One sentence per candidate, at most 25 words, addressed to the GM.
        - Ground every sentence in the candidate's own facts as given. Invent nothing — no
          NPC motives, no events, no details that are not in the material provided.
        - If a candidate gives you nothing to say beyond what its numbers already state, say
          plainly what revealing it would settle. Do not embellish.
        - Never suggest revealing something. The GM decides; you describe the moment.

        Return one entry per candidate id you were given.
        """;

    private readonly IConvergenceGaugeService _gaugeService;
    private readonly IConvergenceNarrationClient _client;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly LoremasterOptions _options;

    public ConvergenceNarrationService(
        IConvergenceGaugeService gaugeService,
        IConvergenceNarrationClient client,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        IOptions<LoremasterOptions> options)
    {
        _gaugeService = gaugeService;
        _client = client;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _options = options.Value;
    }

    public async Task<AppResult<ConvergenceGauge>> NarrateAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        // The gauge's own GM gate is the authority; this fails the same way for the same reason
        // rather than keeping a second copy of the rule.
        var gaugeResult = await _gaugeService.GetGaugeAsync(worldId, actingUserId, role, ct);
        if (!gaugeResult.IsSuccess)
        {
            return gaugeResult;
        }

        var gauge = gaugeResult.Value!;
        if (gauge.Candidates.Count == 0)
        {
            return AppResult<ConvergenceGauge>.Success(gauge);
        }

        // Over budget returns the ranking, not a 402. The GM asked for the page; the sentence
        // was the optional half.
        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            return AppResult<ConvergenceGauge>.Success(gauge);
        }

        var narrated = gauge.Candidates.Take(MaxNarrated).ToList();

        var request = new AiPromptRequest
        {
            SystemPrompt = SystemPrompt,
            UserMessage = BuildUserMessage(narrated),
            Model = _options.AiModel,
            TimeoutSeconds = _options.AiTimeoutSeconds
        };

        ConvergenceNarrationAiResponse response;
        try
        {
            response = await _client.NarrateAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await TrackUsageAsync(worldId, actingUserId, null, false, "ServiceError", ct);
            return AppResult<ConvergenceGauge>.Success(gauge);
        }

        // A null usage means no call was made (the no-op client) — nothing to meter.
        if (response.Usage is not null)
        {
            await TrackUsageAsync(worldId, actingUserId, response.Usage, true, null, ct);
        }

        return AppResult<ConvergenceGauge>.Success(Annotate(gauge, response.Narrations));
    }

    /// <summary>
    /// Attaches sentences by id and changes nothing else. Order, membership, and every score
    /// come out exactly as they went in — a narration for an id that was not asked about is
    /// dropped rather than admitted.
    /// </summary>
    internal static ConvergenceGauge Annotate(
        ConvergenceGauge gauge, IReadOnlyList<ConvergenceNarration> narrations)
    {
        if (narrations.Count == 0)
        {
            return gauge;
        }

        var byId = new Dictionary<Guid, string>();
        foreach (var narration in narrations)
        {
            if (!string.IsNullOrWhiteSpace(narration.Rationale))
            {
                byId[narration.CandidateId] = narration.Rationale.Trim();
            }
        }

        return gauge with
        {
            Candidates = gauge.Candidates
                .Select(c => byId.TryGetValue(c.Id, out var rationale) ? c with { Rationale = rationale } : c)
                .ToList()
        };
    }

    private static string BuildUserMessage(IReadOnlyList<ConvergenceCandidate> candidates)
    {
        var message = new StringBuilder();
        message.AppendLine("Candidates, already ranked, highest first:");
        message.AppendLine();

        foreach (var candidate in candidates)
        {
            message.AppendLine($"id: {candidate.Id}");
            message.AppendLine($"  on: {candidate.AnchorName}");
            message.AppendLine($"  hidden: {candidate.Description}");
            message.AppendLine($"  hidden for {candidate.Components.DaysHidden} days");
            message.AppendLine(candidate.Components.IsSelfContained
                ? "  reveals on its own"
                : $"  brings {candidate.Components.MissingArtifactCount} other entries with it");
            message.AppendLine(
                $"  party already knows {candidate.Components.PartyVisibleFactsOnAnchor} things about it");

            if (candidate.Components.StorylineStatus is { } storylineStatus)
            {
                message.AppendLine($"  its storyline is {storylineStatus}");
            }

            if (candidate.Components.ContradictionSeverity is { } severity)
            {
                message.AppendLine(
                    $"  the party currently believes something this contradicts ({severity})");
            }

            message.AppendLine();
        }

        return message.ToString();
    }

    private Task TrackUsageAsync(
        Guid worldId, Guid userId, AiUsage? usage, bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, userId, AiOperationType.ConvergenceNarration, usage,
            succeeded, errorCode, fallbackModel: _options.AiModel, ct: ct);
}
