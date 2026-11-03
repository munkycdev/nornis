using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <inheritdoc cref="IAdventureDraftPlanService"/>
public class AdventureDraftPlanService : IAdventureDraftPlanService
{
    /// <summary>How many of a section's first words the plan shows, so the GM can see where it starts.</summary>
    public const int OpeningWords = 12;

    private const int EstimatedCharsPerToken = 4;

    private readonly IAdventureRepository _adventures;
    private readonly PrepPromptContextBuilder _prompts;
    private readonly IAdventureDraftingAiClient _ai;
    private readonly IAiBudgetGuard _budget;
    private readonly IAiUsageRecorder _usage;
    private readonly PrepOptions _options;
    private readonly LoremasterOptions _loremaster;
    private readonly ILogger<AdventureDraftPlanService> _logger;

    public AdventureDraftPlanService(
        IAdventureRepository adventures,
        PrepPromptContextBuilder prompts,
        IAdventureDraftingAiClient ai,
        IAiBudgetGuard budget,
        IAiUsageRecorder usage,
        IOptions<PrepOptions> options,
        IOptions<LoremasterOptions> loremaster,
        ILogger<AdventureDraftPlanService> logger)
    {
        _adventures = adventures;
        _prompts = prompts;
        _ai = ai;
        _budget = budget;
        _usage = usage;
        _options = options.Value;
        _loremaster = loremaster.Value;
        _logger = logger;
    }

    private string Model => string.IsNullOrWhiteSpace(_options.Deployment) ? _loremaster.AiModel : _options.Deployment;

    public async Task<AppResult<DraftPlanView>> PlanAsync(PlanDraftCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<DraftPlanView>.Fail(new AppError(403, "insufficient_role", "Prep is for the world's GM."));
        }

        var adventure = await _adventures.GetWithSectionsAsync(command.AdventureId, ct);
        if (adventure is null || adventure.WorldId != command.WorldId)
        {
            return AppResult<DraftPlanView>.Fail(new AppError(404, "not_found", "Adventure not found."));
        }

        if (string.IsNullOrWhiteSpace(adventure.DraftMarkdown))
        {
            return AppResult<DraftPlanView>.Fail(AdventureDesignService.NothingToSplit());
        }

        var draft = adventure.DraftMarkdown;
        var lines = AdventureDraftPlan.LinesOf(draft);
        var headings = AdventureDraftPlanner.FromHeadings(draft);
        if (!command.UseLoremaster)
        {
            var note = headings.Count == 1 && !AdventureDraftPlanner.IsHeadingLine(lines[headings[0].StartLine - 1])
                ? "The draft has no # or ## headings, so it would be one section. Ask the Loremaster to plan it, or add headings."
                : null;
            return AppResult<DraftPlanView>.Success(View(draft, headings, DraftPlanSource.Headings, note, adventure));
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return AppResult<DraftPlanView>.Fail(budgetError);
        }

        var context = await _prompts.BuildAsync(adventure, adventure.Title, command.ActingUserId, command.ActingUserRole, ct);
        var request = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(command.WorldId, command.ActingUserId),
            SystemPrompt = AdventureDraftPlanPrompt.SystemPrompt,
            UserMessage = AdventureDraftPlanPrompt.BuildUserMessage(context, adventure, lines),
            Model = Model,
            TimeoutSeconds = _options.DraftTimeoutSeconds,
        };

        var (answer, failed) = await AskAsync(command, request, ct);
        if (failed)
        {
            return AppResult<DraftPlanView>.Success(View(draft, headings, DraftPlanSource.Headings,
                "The Loremaster couldn't plan the split just now, so this is the plan from your headings.", adventure));
        }

        var plan = AdventureDraftPlanPrompt.ParseAnswer(answer);
        var invalid = plan is null ? "it wasn't a plan" : AdventureDraftPlan.Validate(plan, lines, _options.MaxSections);
        if (invalid is not null)
        {
            _logger.LogWarning("A Loremaster split plan was refused: {Reason}. AdventureId={AdventureId}", invalid, adventure.Id);
            return AppResult<DraftPlanView>.Success(View(draft, headings, DraftPlanSource.Headings,
                "The Loremaster's plan couldn't be used, so this is the plan from your headings.", adventure));
        }

        return AppResult<DraftPlanView>.Success(View(draft, plan!, DraftPlanSource.Loremaster, null, adventure));
    }

    /// <summary>The plan with each row's opening words, read from the cut itself so the preview is what applying makes.</summary>
    private static DraftPlanView View(string draft, IReadOnlyList<DraftSplitPoint> plan, DraftPlanSource source, string? note, Adventure adventure)
    {
        var cut = AdventureDraftCutter.Cut(draft, plan);
        var rows = plan.Select((p, i) => new DraftPlanRow(p.StartLine, p.Title, p.Kind, Opening(cut[i].BodyMarkdown))).ToList();
        return new DraftPlanView(rows, source, note, adventure.DraftUpdatedAt);
    }

    private static string Opening(string body)
    {
        var words = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= OpeningWords ? string.Join(' ', words) : string.Join(' ', words.Take(OpeningWords)) + " …";
    }

    /// <summary>One call, read whole. A failure is logged and recorded, and the caller falls back to the headings.</summary>
    private async Task<(string Answer, bool Failed)> AskAsync(PlanDraftCommand command, AiPromptRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();
        AiUsage? usage = null;
        string? failureCode = null;
        try
        {
            await foreach (var chunk in _ai.StreamAsync(request, ct))
            {
                usage = chunk.Usage ?? usage;
                text.Append(chunk.Text);
                if (chunk.CutOff)
                {
                    failureCode = "CutOff";
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await RecordAsync(command, request, text, usage, stopwatch.Elapsed, succeeded: true, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A Loremaster split plan failed. AdventureId={AdventureId}", command.AdventureId);
            failureCode = "ServiceError";
        }

        await RecordAsync(command, request, text, usage, stopwatch.Elapsed, failureCode is null or "CutOff", failureCode);
        return (text.ToString(), failureCode is not null);
    }

    private Task RecordAsync(
        PlanDraftCommand command, AiPromptRequest request, StringBuilder text, AiUsage? reported,
        TimeSpan elapsed, bool succeeded, string? errorCode)
    {
        var usage = (reported ?? Estimate(request, text)) with { DurationMs = (int)elapsed.TotalMilliseconds };
        return _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.AdventureDrafting, usage,
            succeeded, errorCode, fallbackModel: request.Model, ct: CancellationToken.None);
    }

    private static AiUsage Estimate(AiPromptRequest request, StringBuilder text)
    {
        var input = text.Length > 0 ? (request.SystemPrompt.Length + request.UserMessage.Length) / EstimatedCharsPerToken : 0;
        var output = text.Length / EstimatedCharsPerToken;
        return new AiUsage { Model = request.Model, InputTokens = input, OutputTokens = output, TotalTokens = input + output, DurationMs = 0 };
    }
}
