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

/// <inheritdoc cref="IAdventureConversationService"/>
public class AdventureConversationService : IAdventureConversationService
{
    /// <summary>The instruction a Put in section revision carries, so the section's history says where the text came from.</summary>
    public const string PutInSectionInstruction = "Put in from the conversation";

    /// <summary>Rough chars-per-token for a stream that ended without reported usage, as drafting uses.</summary>
    private const int EstimatedCharsPerToken = 4;

    private readonly IAdventureRepository _adventures;
    private readonly IConversationRepository _conversations;
    private readonly IAdventureDesignService _design;
    private readonly PrepPromptContextBuilder _prompts;
    private readonly IAdventureDraftingAiClient _ai;
    private readonly IAiBudgetGuard _budget;
    private readonly IAiUsageRecorder _usage;
    private readonly ISectionDraftGate _gate;
    private readonly IAiFundingService _funding;
    private readonly AiModelsOptions _models;
    private readonly PrepOptions _options;
    private readonly LoremasterOptions _loremaster;
    private readonly ILogger<AdventureConversationService> _logger;

    public AdventureConversationService(
        IAdventureRepository adventures,
        IConversationRepository conversations,
        IAdventureDesignService design,
        PrepPromptContextBuilder prompts,
        IAdventureDraftingAiClient ai,
        IAiBudgetGuard budget,
        IAiUsageRecorder usage,
        ISectionDraftGate gate,
        IAiFundingService funding,
        IOptions<AiModelsOptions> models,
        IOptions<PrepOptions> options,
        IOptions<LoremasterOptions> loremaster,
        ILogger<AdventureConversationService> logger)
    {
        _funding = funding;
        _models = models.Value;
        _adventures = adventures;
        _conversations = conversations;
        _design = design;
        _prompts = prompts;
        _ai = ai;
        _budget = budget;
        _usage = usage;
        _gate = gate;
        _options = options.Value;
        _loremaster = loremaster.Value;
        _logger = logger;
    }

    /// <summary>The deployment a turn runs on: drafting's, so the two share a price and a ceiling.</summary>
    private string Model => string.IsNullOrWhiteSpace(_options.Deployment) ? _loremaster.AiModel : _options.Deployment;

    public async Task<AppResult<ConversationView>> GetAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<ConversationView>.Fail(GmOnly());
        }

        if (await LoadAdventureAsync(worldId, adventureId, ct) is null)
        {
            return AppResult<ConversationView>.Fail(NotFound());
        }

        var conversation = await _conversations.GetByAdventureAsync(adventureId, ct);
        var messages = conversation?.Messages.ToList() ?? [];
        return AppResult<ConversationView>.Success(new ConversationView(
            messages, IsFull(messages.Count), await WebResearchAvailabilityAsync(worldId, ct)));
    }

    public async Task<AppResult<IAsyncEnumerable<ConversationEvent>>> SendAsync(
        SendConversationMessageCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return Fail(GmOnly());
        }

        var text = command.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return Fail(new AppError(400, "validation_error", "Say something to the Loremaster first."));
        }

        if (text.Length > _options.MaxConversationMessageChars)
        {
            return Fail(PrepBounds.TooLarge(
                $"That message is {text.Length:N0} characters; a message can be at most {_options.MaxConversationMessageChars:N0}."));
        }

        var adventure = await LoadAdventureAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null
            || (command.SectionId is { } sectionId && adventure.Sections.All(s => s.Id != sectionId)))
        {
            return Fail(NotFound());
        }

        var conversation = await _conversations.GetByAdventureAsync(adventure.Id, ct);
        var earlier = conversation?.Messages.ToList() ?? [];
        if (IsFull(earlier.Count))
        {
            return Fail(new AppError(409, "conversation_full",
                $"This conversation holds {earlier.Count:N0} messages, which is as many as it can. Clear it to start again."));
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        // Before the GM's message is saved, so a page that showed the switch on a key that has
        // since gone can't send a search turn nothing can pay for.
        if (command.WebResearch
            && await WebResearchAvailabilityAsync(command.WorldId, ct) is var availability
            && WebResearchAvailability.Reason(availability) is { } unavailable)
        {
            return Fail(new AppError(409, "web_research_unavailable", unavailable));
        }

        // Keyed on the Adventure, not a section: a turn and a draft can run side by side, two
        // turns can't. Section and Adventure ids are both Guids, so the keys never meet.
        var lease = _gate.TryEnter(adventure.Id);
        if (lease is null)
        {
            return Fail(AnswerInProgress());
        }

        try
        {
            // Saved before the model is called, so a failed answer never loses the question.
            var now = DateTimeOffset.UtcNow;
            var question = new ConversationMessage
            {
                Id = Guid.NewGuid(),
                Role = ConversationRole.User,
                Content = text,
                CreatedAt = now,
            };

            if (conversation is null)
            {
                conversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    WorldId = command.WorldId,
                    UserId = command.ActingUserId,
                    AdventureId = adventure.Id,
                    Title = adventure.Title,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                question.ConversationId = conversation.Id;
                conversation.Messages = [question];
                await _conversations.CreateAsync(conversation, ct);
            }
            else
            {
                question.ConversationId = conversation.Id;
                await _conversations.AddMessageAsync(question, ct);
            }

            // In Design mode the draft rides instead of a section (feature 37).
            var onDraft = command.Scope == ConversationScope.Draft;
            var section = onDraft ? null : adventure.Sections.FirstOrDefault(s => s.Id == command.SectionId);
            var context = await _prompts.BuildAsync(
                adventure, RetrievalText(adventure, section, text), command.ActingUserId, command.ActingUserRole, ct);

            var request = new AiPromptRequest
            {
                Scope = AiCallScope.ForWorld(command.WorldId, command.ActingUserId),
                SystemPrompt = command.WebResearch
                    ? AdventureConversationPrompt.ResearchSystemPrompt(_options.MaxWebSearchesPerTurn)
                    : AdventureConversationPrompt.SystemPrompt,
                UserMessage = AdventureConversationPrompt.BuildUserMessage(new AdventureConversationInput(
                    context,
                    adventure,
                    section?.Id,
                    AdventureConversationPrompt.HistoryWindow(earlier, _options.ConversationHistoryChars),
                    text,
                    _options.ConversationSectionChars,
                    onDraft)),
                Model = Model,
                TimeoutSeconds = TimeoutFor(onDraft, command.WebResearch),
                WebSearch = command.WebResearch
                    ? new AiWebSearchRequest(_options.MaxWebSearchesPerTurn, _options.WebResearchOutputCeilingTokens)
                    : null,

                // A Design turn may write the whole draft (feature 41), so it has room to.
                OutputCeilingTokens = onDraft
                    ? Math.Max(_options.DesignOutputCeilingTokens, command.WebResearch ? _options.WebResearchOutputCeilingTokens : 0)
                    : null,
            };

            return AppResult<IAsyncEnumerable<ConversationEvent>>.Success(
                StreamAsync(command, conversation.Id, request, onDraft, lease, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lease.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            lease.Dispose();
            _logger.LogError(ex, "Prep conversation turn setup failed. WorldId={WorldId} AdventureId={AdventureId}",
                command.WorldId, command.AdventureId);
            return Fail(new AppError(500, "internal_error", "Something went wrong. Please try again."));
        }
    }

    public async Task<AppResult> ClearAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult.Fail(GmOnly());
        }

        if (await LoadAdventureAsync(worldId, adventureId, ct) is null)
        {
            return AppResult.Fail(NotFound());
        }

        // Not while an answer is being written: it would have nowhere to be saved.
        using var lease = _gate.TryEnter(adventureId);
        if (lease is null)
        {
            return AppResult.Fail(AnswerInProgress());
        }

        await _conversations.DeleteByAdventureAsync(adventureId, ct);
        return AppResult.Success();
    }

    public async Task<AppResult<PutInSectionResult>> PutInSectionAsync(PutInSectionCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<PutInSectionResult>.Fail(GmOnly());
        }

        var adventure = await LoadAdventureAsync(command.WorldId, command.AdventureId, ct);
        var section = adventure?.Sections.FirstOrDefault(s => s.Id == command.SectionId);
        if (adventure is null || section is null)
        {
            return AppResult<PutInSectionResult>.Fail(NotFound());
        }

        // Only an answer, and only one from this Adventure's own conversation.
        var message = await _conversations.GetMessageAsync(command.MessageId, ct);
        if (message is null
            || message.Role != ConversationRole.Assistant
            || message.Conversation.AdventureId != adventure.Id
            || message.Conversation.WorldId != command.WorldId)
        {
            return AppResult<PutInSectionResult>.Fail(new AppError(404, "not_found",
                "That answer isn't in this adventure's conversation."));
        }

        if (section.UpdatedAt != command.BasedOnUpdatedAt)
        {
            return AppResult<PutInSectionResult>.Fail(SectionChanged());
        }

        var answer = message.Content.Trim();

        // An unwritten section holds the skeleton's template text, which must never survive above
        // real text, so Append onto one replaces it.
        var body = command.Mode == PutInSectionMode.Append && !AdventureSkeleton.IsUnfilled(section.BodyMarkdown)
            ? section.BodyMarkdown.TrimEnd() + "\n\n" + answer
            : answer;

        var after = adventure.Sections.Select(s => s.Id == section.Id ? WithBody(s, body) : s).ToList();
        if (PrepBounds.Validate(after, _options) is { } tooLarge)
        {
            return AppResult<PutInSectionResult>.Fail(tooLarge);
        }

        var now = DateTimeOffset.UtcNow;
        var revision = new AdventureSectionRevision
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            Instruction = PutInSectionInstruction,
            Model = message.Model ?? Model,
            CreatedAt = now,
        };

        // The draft's accept: the revision, the trim and the stamp check are the same code, so
        // Undo gives back what was there.
        // A research answer's text records that the Adventure drew on the web (feature 40), in the same save.
        if (!await _adventures.TryAcceptDraftAsync(
            revision, body, _options.MaxRevisionsPerSection, command.BasedOnUpdatedAt, now, researchedOnWeb: message.WebResearch is not null, ct))
        {
            return AppResult<PutInSectionResult>.Fail(SectionChanged());
        }

        var counts = await _adventures.CountRevisionsAsync(adventure.Id, ct);
        section.BodyMarkdown = body;
        section.UpdatedAt = now;
        return AppResult<PutInSectionResult>.Success(new PutInSectionResult(section, counts.GetValueOrDefault(section.Id)));
    }

    /// <summary>A turn's timeout: a Design turn's (feature 41) or a research turn's, whichever is longer, else drafting's.</summary>
    private int TimeoutFor(bool onDraft, bool webResearch) =>
        Math.Max(
            onDraft ? _options.DesignTimeoutSeconds : 0,
            webResearch ? _options.WebResearchTimeoutSeconds : _options.DraftTimeoutSeconds);

    /// <summary>A turn adds two messages, the GM's and the answer, so it needs room for both.</summary>
    private bool IsFull(int messageCount) => messageCount + 2 > _options.MaxConversationMessages;

    /// <summary>What the retriever name-matches against: the message first, then where the GM is.</summary>
    private static string RetrievalText(Adventure adventure, AdventureSection? section, string message) =>
        string.Join('\n', new[] { message, adventure.Title, section?.Title }.Where(p => !string.IsNullOrWhiteSpace(p)));

    private async IAsyncEnumerable<ConversationEvent> StreamAsync(
        SendConversationMessageCommand command,
        Guid conversationId,
        AiPromptRequest request,
        bool onDraft,
        IDisposable lease,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var _ = lease;

        // A Design turn's edits go into the draft, so only its prose streams to the chat (feature 41).
        var shown = onDraft ? new DraftEditStream() : null;
        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();
        AiUsage? usage = null;
        WebResearchResult? research = null;
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
                    throw; // the GM stopped it; the finally still writes the ledger row, and nothing else
                }
                catch (Exception ex)
                {
                    (failureCode, failure) = Classify(ex);
                    break;
                }

                usage = chunk.Usage ?? usage;
                research = chunk.WebResearch ?? research;
                cutOff |= chunk.CutOff;
                if (chunk.Text.Length > 0)
                {
                    text.Append(chunk.Text);
                    if (shown is null)
                    {
                        yield return new ConversationEvent.Delta(chunk.Text);
                    }
                    else
                    {
                        var (prose, editsOpened) = shown.Push(chunk.Text);
                        if (prose.Length > 0)
                        {
                            yield return new ConversationEvent.Delta(prose);
                        }

                        if (editsOpened > 0)
                        {
                            yield return new ConversationEvent.WritingDraft();
                        }
                    }
                }
            }

            if (shown?.Flush() is { Prose.Length: > 0 } rest)
            {
                yield return new ConversationEvent.Delta(rest.Prose);
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

            // A stream cut short by the reader or a failure records here; one that ran to its end
            // records after the save below.
            if (!finished || failure is not null)
            {
                await RecordAsync(command, request, text, usage, stopwatch.Elapsed, finished, failureCode);
            }
        }

        if (failure is not null)
        {
            yield return new ConversationEvent.Failed(failure);
            yield break;
        }

        // A research turn's answer always carries its result, even when the provider reported
        // nothing: null on a message means it wasn't a research turn.
        var webResearch = request.WebSearch is null ? null : research ?? WebResearchResult.Create([], null, null);

        // An edit left open means the answer stopped partway through writing it, however the
        // provider ended the stream (feature 41).
        cutOff |= onDraft && DraftEdits.HasUnclosedFence(text.ToString());
        var (answer, refusal) = await SaveAnswerAsync(command, conversationId, text.ToString(), cutOff, webResearch, onDraft);
        await RecordAsync(command, request, text, usage, stopwatch.Elapsed, finished: true, refusal is null ? null : "Refused");

        yield return refusal is null
            ? new ConversationEvent.Completed(answer!)
            : new ConversationEvent.Failed(refusal);
    }

    /// <summary>
    /// Saves an answer that ended cleanly. A cut-off one is refused rather than saved half-said,
    /// and an empty one has nothing to save. A Design turn's edits are applied to the draft first
    /// and saved with their outcomes (feature 41). Runs to completion even if the GM leaves now.
    /// </summary>
    private async Task<(ConversationMessage? Answer, AppError? Refusal)> SaveAnswerAsync(
        SendConversationMessageCommand command, Guid conversationId, string text, bool cutOff, WebResearchResult? webResearch,
        bool onDraft)
    {
        if (cutOff)
        {
            return (null, new AppError(502, "answer_cut_off",
                "The answer ran past the length one answer can be and was cut off, so it wasn't saved. "
                + "Try asking for less at once."));
        }

        var content = text.Trim();
        if (webResearch is not null)
        {
            content = WebPageLinks.Strip(content, webResearch.Pages);
        }

        if (content.Length == 0)
        {
            return (null, new AppError(502, "answer_empty", "The answer came back empty, so nothing was saved. Please try again."));
        }

        var answer = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = ConversationRole.Assistant,
            Content = content,
            Model = Model,
            WebResearch = webResearch,
            DraftEditOutcomes = onDraft ? await ApplyEditsAsync(command, content, webResearch is not null) : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _conversations.AddMessageAsync(answer, CancellationToken.None);
        return (answer, null);
    }

    /// <summary>
    /// A Design answer's edits applied to the draft, as one change (feature 41); null when it has
    /// none. Never fails the turn: an answer whose edits couldn't be applied is still saved, with
    /// every edit Not applied, so the GM can apply them by hand.
    /// </summary>
    private async Task<IReadOnlyList<DraftEditOutcome>?> ApplyEditsAsync(
        SendConversationMessageCommand command, string content, bool researchedOnWeb)
    {
        var edits = DraftEdits.Parse(content);
        if (edits.Count == 0)
        {
            return null;
        }

        try
        {
            var applied = await _design.ApplyAnswerEditsAsync(new ApplyAnswerEditsCommand(
                command.WorldId, command.AdventureId, command.ActingUserRole, edits, researchedOnWeb), CancellationToken.None);
            if (applied.IsSuccess)
            {
                return applied.Value!.Outcomes;
            }

            _logger.LogWarning("A Design turn's edits weren't applied: {Code}. AdventureId={AdventureId}",
                applied.Error!.Code, command.AdventureId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying a Design turn's edits failed. AdventureId={AdventureId}", command.AdventureId);
        }

        return edits.Select(_ => DraftEditOutcome.NotApplied).ToList();
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
    /// One ledger row per turn, under its own operation in the Prep pool. Recorded as drafting's
    /// are: a stopped or refused answer still counts, estimated when the provider reported none.
    /// </summary>
    private Task RecordAsync(
        SendConversationMessageCommand command, AiPromptRequest request, StringBuilder text, AiUsage? reported,
        TimeSpan elapsed, bool finished, string? failureCode)
    {
        var usage = reported ?? Estimate(request, text);
        usage = usage with { DurationMs = (int)elapsed.TotalMilliseconds };

        var succeeded = !finished || failureCode is null or "Refused";
        var errorCode = finished ? failureCode : "Cancelled";

        return _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.AdventureConversation, usage,
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
        AiWebSearchUnsupportedException => ("WebSearchUnsupported", new AppError(422, "web_research_unsupported",
            "This model can't search the web. Turn Web research off, or pick another Prep model under Profile, Models.")),
        AiRefusalException refusal => (refusal.Category, refusal.ToAppError()),
        AiHttpException { StatusCode: 429 } =>
            ("RateLimited", new AppError(429, "rate_limited", "Too many requests. Please try again in a moment.")),
        AiTimeoutException or OperationCanceledException =>
            ("Timeout", Unavailable),
        _ => ("ServiceError", Unavailable),
    };

    private static readonly AppError Unavailable =
        new(503, "service_unavailable", "The Loremaster is temporarily unavailable, so no answer was saved. Please try again.");

    /// <summary>
    /// Whether a Prep turn in this world can search now: who would pay, asked of the funding rule
    /// without spending anything. Nothing can pay at all reads as needing a key of one's own.
    /// </summary>
    private async Task<WebResearchAvailabilityKind> WebResearchAvailabilityAsync(Guid worldId, CancellationToken ct)
    {
        try
        {
            var funding = await _funding.ResolveAsync(AiCallScope.ForWorld(worldId), AiTask.Prep, Model, needsImages: false, ct);
            return WebResearchAvailability.For(funding, _models);
        }
        catch (AiRefusalException)
        {
            return WebResearchAvailabilityKind.NeedsOwnKey;
        }
    }

    private async Task<Adventure?> LoadAdventureAsync(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var adventure = await _adventures.GetWithSectionsAsync(adventureId, ct);
        return adventure is not null && adventure.WorldId == worldId ? adventure : null;
    }

    private static AppError GmOnly() => new(403, "insufficient_role", "Prep is for the world's GM.");

    private static AppError NotFound() => new(404, "not_found", "Adventure not found.");

    private static AppError AnswerInProgress() => new(409, "answer_in_progress",
        "The Loremaster is still answering. Wait for it, or stop it, first.");

    private static AppError SectionChanged() => new(409, "section_changed",
        "This section changed since you opened it. Reload it to see the latest version.");

    private static AppResult<IAsyncEnumerable<ConversationEvent>> Fail(AppError error) =>
        AppResult<IAsyncEnumerable<ConversationEvent>>.Fail(error);
}
