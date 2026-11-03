using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <inheritdoc cref="IAdventureDraftingService"/>
public class AdventureDraftingService : IAdventureDraftingService
{
    /// <summary>Rough chars-per-token for a stream that ended without reported usage, as Ask uses.</summary>
    private const int EstimatedCharsPerToken = 4;

    private readonly IAdventureRepository _adventures;
    private readonly IWorldRepository _worlds;
    private readonly IKnowledgeRetriever _retriever;
    private readonly IAdventureDraftingAiClient _ai;
    private readonly IAiBudgetGuard _budget;
    private readonly IAiUsageRecorder _usage;
    private readonly ISectionDraftGate _gate;
    private readonly IConvergenceGaugeService _gauge;
    private readonly PrepOptions _options;
    private readonly LoremasterOptions _loremaster;
    private readonly ILogger<AdventureDraftingService> _logger;

    public AdventureDraftingService(
        IAdventureRepository adventures,
        IWorldRepository worlds,
        IKnowledgeRetriever retriever,
        IAdventureDraftingAiClient ai,
        IAiBudgetGuard budget,
        IAiUsageRecorder usage,
        ISectionDraftGate gate,
        IConvergenceGaugeService gauge,
        IOptions<PrepOptions> options,
        IOptions<LoremasterOptions> loremaster,
        ILogger<AdventureDraftingService> logger)
    {
        _adventures = adventures;
        _worlds = worlds;
        _retriever = retriever;
        _ai = ai;
        _budget = budget;
        _usage = usage;
        _gate = gate;
        _gauge = gauge;
        _options = options.Value;
        _loremaster = loremaster.Value;
        _logger = logger;
    }

    /// <summary>The deployment drafting runs on, and the name its spend is priced under.</summary>
    private string Model => string.IsNullOrWhiteSpace(_options.Deployment) ? _loremaster.AiModel : _options.Deployment;

    public async Task<AppResult<IAsyncEnumerable<DraftEvent>>> DraftAsync(DraftSectionCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return Fail(GmOnly());
        }

        var instruction = string.IsNullOrWhiteSpace(command.Instruction) ? null : command.Instruction.Trim();
        if (instruction is not null && instruction.Length > _options.MaxInstructionChars)
        {
            return Fail(new AppError(400, "validation_error",
                $"An instruction can be at most {_options.MaxInstructionChars:N0} characters."));
        }

        var adventure = await _adventures.GetWithSectionsAsync(command.AdventureId, ct);
        var section = adventure is not null && adventure.WorldId == command.WorldId
            ? adventure.Sections.FirstOrDefault(s => s.Id == command.SectionId)
            : null;
        if (adventure is null || section is null)
        {
            return Fail(new AppError(404, "not_found", "Adventure not found."));
        }

        if (section.UpdatedAt != command.BasedOnUpdatedAt)
        {
            return Fail(SectionChanged());
        }

        if (section.Kind == AdventureSectionKind.Overview && AdventureSkeleton.IsUnfilled(section.BodyMarkdown)
            && string.IsNullOrWhiteSpace(adventure.Pitch) && instruction is null)
        {
            return Fail(new AppError(400, "instruction_required",
                "Say what the adventure is about, here or as its pitch, so the Overview has something to start from."));
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        var lease = _gate.TryEnter(section.Id);
        if (lease is null)
        {
            return Fail(new AppError(409, "draft_in_progress", "A draft of this section is already being written."));
        }

        try
        {
            var world = await _worlds.GetByIdAsync(command.WorldId, ct);
            var context = await _retriever.RetrieveAsync(
                RetrievalText(adventure, section, instruction), command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
            var threads = section.Kind == AdventureSectionKind.Overview ? await ThreadsAsync(command, ct) : [];

            var request = new AiPromptRequest
            {
                SystemPrompt = AdventureDraftPrompt.SystemPrompt,
                UserMessage = AdventureDraftPrompt.BuildUserMessage(new AdventureDraftInput(
                    PrintDetails.SystemOf(adventure.Details, world?.GameSystem),
                    LoremasterService.FormatKnowledgeContext(context, _loremaster.MaxSessionChars, _loremaster.MaxContextTokens),
                    adventure,
                    section.Id,
                    instruction,
                    threads)),
                Model = Model,
                TimeoutSeconds = _options.DraftTimeoutSeconds,
            };

            return AppResult<IAsyncEnumerable<DraftEvent>>.Success(
                StreamAsync(command, adventure, section, instruction, request, lease, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lease.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            lease.Dispose();
            _logger.LogError(ex, "Prep draft context assembly failed. WorldId={WorldId} SectionId={SectionId}",
                command.WorldId, command.SectionId);
            return Fail(new AppError(500, "internal_error", "Something went wrong. Please try again."));
        }
    }

    /// <summary>
    /// Threads to use, for an Overview draft: the convergence gauge's strongest candidates, read
    /// at the GM's role. No AI call (the gauge's narration is never asked for). A gauge that
    /// fails costs the draft its threads, never the draft. The prompt owns the cap; taking it
    /// here only saves copying candidates the prompt would drop.
    /// </summary>
    private async Task<IReadOnlyList<PrepThread>> ThreadsAsync(DraftSectionCommand command, CancellationToken ct)
    {
        try
        {
            var gauge = await _gauge.GetGaugeAsync(command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
            return gauge.IsSuccess
                ? gauge.Value!.Candidates.Take(AdventureDraftPrompt.MaxThreads).Select(c => new PrepThread(c.AnchorName, c.Description)).ToList()
                : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Threads to use were left out of an Overview draft. WorldId={WorldId}", command.WorldId);
            return [];
        }
    }

    public async Task<AppResult<PrepBudgetStatus>> GetBudgetAsync(Guid worldId, WorldRole role, CancellationToken ct) =>
        role != WorldRole.GM
            ? AppResult<PrepBudgetStatus>.Fail(GmOnly())
            : AppResult<PrepBudgetStatus>.Success(await _budget.GetPrepStatusAsync(worldId, ct));

    /// <summary>
    /// What the retriever name-matches against. The pitch, the titles and the instruction, as the
    /// design names them, plus the section's own text when it has some: a revision of a scene
    /// that names Captain Voss should bring Voss's record with it.
    /// </summary>
    private static string RetrievalText(Adventure adventure, AdventureSection section, string? instruction)
    {
        var parts = new List<string?> { adventure.Title, adventure.Pitch, section.Title, instruction };
        if (!AdventureSkeleton.IsUnfilled(section.BodyMarkdown))
        {
            parts.Add(section.BodyMarkdown);
        }

        return string.Join('\n', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private async IAsyncEnumerable<DraftEvent> StreamAsync(
        DraftSectionCommand command,
        Adventure adventure,
        AdventureSection section,
        string? instruction,
        AiPromptRequest request,
        IDisposable lease,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var _ = lease;
        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();
        AiUsage? usage = null;
        var cutOff = false;
        string? failureCode = null;
        AppError? failure = null;
        var finished = false;

        IAsyncEnumerator<AiStreamChunk>? chunks = null;
        try
        {
            while (true)
            {
                AiStreamChunk chunk;
                try
                {
                    chunks ??= _ai.StreamAsync(request, ct).GetAsyncEnumerator(ct);
                    if (!await chunks.MoveNextAsync())
                    {
                        break;
                    }

                    chunk = chunks.Current;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // the GM left; the finally still writes the ledger row, and nothing else
                }
                catch (Exception ex)
                {
                    (failureCode, failure) = Classify(ex);
                    break;
                }

                usage = chunk.Usage ?? usage;
                cutOff |= chunk.CutOff;
                if (chunk.Text.Length > 0)
                {
                    text.Append(chunk.Text);
                    yield return new DraftEvent.Delta(chunk.Text);
                }
            }

            finished = true;
        }
        finally
        {
            if (chunks is not null)
            {
                await chunks.DisposeAsync();
            }

            stopwatch.Stop();

            // Only a stream cut short by the reader or a failure records here; a stream that ran
            // to its end records after the accept below, so the order is write, then ledger.
            if (!finished || failure is not null)
            {
                await RecordAsync(command, request, text, usage, stopwatch.Elapsed, finished, failureCode);
            }
        }

        if (failure is not null)
        {
            yield return new DraftEvent.Failed(failure);
            yield break;
        }

        // The stream ran to its end, so the text is paid for. The accept runs to completion even if
        // the GM leaves now: nothing is lost that Undo can't give back.
        var (saved, revisionCount, refusal) = await AcceptAsync(command, adventure, section, instruction, text.ToString(), cutOff);
        await RecordAsync(command, request, text, usage, stopwatch.Elapsed, finished: true, refusal is null ? null : "Refused");
        if (usage?.ReasoningTokens is > 0)
        {
            _logger.LogInformation("Prep draft spent {ReasoningTokens} of {OutputTokens} output tokens reasoning. SectionId={SectionId}",
                usage.ReasoningTokens, usage.OutputTokens, section.Id);
        }

        yield return refusal is null
            ? new DraftEvent.Completed(saved!, revisionCount)
            : new DraftEvent.Failed(refusal);
    }

    private async Task<(AdventureSection? Saved, int RevisionCount, AppError? Refusal)> AcceptAsync(
        DraftSectionCommand command, Adventure adventure, AdventureSection section, string? instruction, string text, bool cutOff)
    {
        if (cutOff)
        {
            return (null, 0, new AppError(502, "draft_cut_off",
                "The draft ran past the length a section can be drafted at and was cut off, so nothing was saved. "
                + "Try asking for less, or split the section."));
        }

        var body = text.Trim();
        if (body.Length == 0)
        {
            return (null, 0, new AppError(502, "draft_empty", "The draft came back empty, so nothing changed. Please try again."));
        }

        var after = adventure.Sections.Select(s => s.Id == section.Id ? WithBody(s, body) : s).ToList();
        if (PrepBounds.Validate(after, _options) is { } tooLarge)
        {
            return (null, 0, tooLarge);
        }

        var now = DateTimeOffset.UtcNow;
        var revision = new AdventureSectionRevision
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            Instruction = instruction,
            Model = Model,
            CreatedAt = now,
        };

        if (!await _adventures.TryAcceptDraftAsync(revision, body, _options.MaxRevisionsPerSection, command.BasedOnUpdatedAt, now, CancellationToken.None))
        {
            return (null, 0, new AppError(409, "section_changed",
                "This section changed while the draft was being written, so the draft wasn't saved."));
        }

        var counts = await _adventures.CountRevisionsAsync(adventure.Id, CancellationToken.None);
        section.BodyMarkdown = body;
        section.UpdatedAt = now;
        return (section, counts.GetValueOrDefault(section.Id), null);
    }

    private static AdventureSection WithBody(AdventureSection s, string body) => new()
    {
        Id = s.Id,
        AdventureId = s.AdventureId,
        Kind = s.Kind,
        Title = s.Title,
        BodyMarkdown = body,
        SortOrder = s.SortOrder,
        UpdatedAt = s.UpdatedAt,
    };

    /// <summary>
    /// One ledger row per call, under the Prep pool's operation. A stream with no reported usage
    /// that wrote something is estimated, as cancelled Ask streams are; one that wrote nothing
    /// stays at zero. A GM leaving is a success as far as the provider is concerned.
    /// </summary>
    private Task RecordAsync(
        DraftSectionCommand command, AiPromptRequest request, StringBuilder text, AiUsage? reported,
        TimeSpan elapsed, bool finished, string? failureCode)
    {
        var usage = reported ?? Estimate(request, text);
        usage = usage with { DurationMs = (int)elapsed.TotalMilliseconds };

        // A refusal after a full answer (cut off, empty, too large, changed underneath) is the
        // provider doing its job, so it counts as a success for the outcome monitor.
        var succeeded = !finished || failureCode is null or "Refused";
        var errorCode = finished ? failureCode : "Cancelled";

        return _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.AdventureDrafting, usage,
            succeeded, errorCode, fallbackModel: request.Model, ct: CancellationToken.None);
    }

    private static AiUsage Estimate(AiPromptRequest request, StringBuilder text)
    {
        var spent = text.Length > 0;
        var input = spent ? (request.SystemPrompt.Length + request.UserMessage.Length) / EstimatedCharsPerToken : 0;
        var output = text.Length / EstimatedCharsPerToken;
        return new AiUsage
        {
            Model = request.Model,
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output,
            DurationMs = 0,
        };
    }

    private static (string Code, AppError Error) Classify(Exception ex) => ex switch
    {
        AiHttpException { StatusCode: 429 } =>
            ("RateLimited", new AppError(429, "rate_limited", "Too many requests. Please try again in a moment.")),
        AiTimeoutException or OperationCanceledException =>
            ("Timeout", Unavailable),
        _ => ("ServiceError", Unavailable),
    };

    private static readonly AppError Unavailable =
        new(503, "service_unavailable", "Drafting is temporarily unavailable. Nothing was changed. Please try again.");

    private static AppError GmOnly() => new(403, "insufficient_role", "Prep is for the world's GM.");

    private static AppError SectionChanged() => new(409, "section_changed",
        "This section changed since you opened it. Reload it to see the latest version.");

    private static AppResult<IAsyncEnumerable<DraftEvent>> Fail(AppError error) =>
        AppResult<IAsyncEnumerable<DraftEvent>>.Fail(error);
}
