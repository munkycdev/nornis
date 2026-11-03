using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <inheritdoc cref="IAdventureDesignService"/>
public class AdventureDesignService : IAdventureDesignService
{
    /// <summary>The most source books one Adventure reads; more would crowd every prompt's passages.</summary>
    public const int MaxSourceBooks = 5;

    private readonly IAdventureRepository _adventures;
    private readonly ILibraryDocumentRepository _documents;
    private readonly IAdventureService _views;
    private readonly IConversationRepository _conversations;
    private readonly PrepOptions _options;
    private readonly ILogger<AdventureDesignService> _logger;

    public AdventureDesignService(
        IAdventureRepository adventures, ILibraryDocumentRepository documents, IAdventureService views,
        IConversationRepository conversations, IOptions<PrepOptions> options, ILogger<AdventureDesignService> logger)
    {
        _logger = logger;
        _adventures = adventures;
        _documents = documents;
        _views = views;
        _conversations = conversations;
        _options = options.Value;
    }

    public async Task<AppResult<AdventureDraftState>> SaveDraftAsync(SaveDraftCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureDraftState>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventureDraftState>.Fail(NotFound());
        }

        var markdown = command.Markdown ?? "";
        if (markdown.Length > Adventure.MaxDraftChars)
        {
            return AppResult<AdventureDraftState>.Fail(DraftTooLarge(markdown.Length));
        }

        var now = DateTimeOffset.UtcNow;
        if (!await _adventures.TrySaveDraftAsync(
            adventure.Id, markdown, revision: null, maxRevisions: 0, command.BasedOnUpdatedAt, now, loremasterWrote: false, ct))
        {
            return AppResult<AdventureDraftState>.Fail(DraftChanged());
        }

        return AppResult<AdventureDraftState>.Success(
            new(markdown, now, await _adventures.CountDraftRevisionsAsync(adventure.Id, ct)));
    }

    public async Task<AppResult<AdventureDraftState>> UndoDraftAsync(UndoDraftCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureDraftState>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventureDraftState>.Fail(NotFound());
        }

        var revision = await _adventures.GetLatestDraftRevisionAsync(adventure.Id, ct);
        if (revision is null)
        {
            return AppResult<AdventureDraftState>.Fail(new AppError(409, "nothing_to_undo",
                "The draft has no change to undo."));
        }

        var now = DateTimeOffset.UtcNow;
        if (!await _adventures.TryRestoreDraftRevisionAsync(revision, adventure.DraftUpdatedAt, now, ct))
        {
            return AppResult<AdventureDraftState>.Fail(DraftChanged());
        }

        return AppResult<AdventureDraftState>.Success(
            new(revision.BodyMarkdown, now, await _adventures.CountDraftRevisionsAsync(adventure.Id, ct)));
    }

    public async Task<AppResult<AdventureDraftState>> ApplyEditAsync(ApplyDraftEditCommand command, CancellationToken ct)
    {
        var (adventure, answer, error) = await LoadAnswerAsync(
            command.WorldId, command.AdventureId, command.ActingUserRole, command.MessageId, ct);
        if (error is not null)
        {
            return AppResult<AdventureDraftState>.Fail(error);
        }

        var edits = DraftEdits.Parse(answer!.Content);
        if (command.Index < 0 || command.Index >= edits.Count)
        {
            return AppResult<AdventureDraftState>.Fail(new AppError(404, "not_found", "That answer has no such edit."));
        }

        if (adventure!.DraftUpdatedAt != command.BasedOnUpdatedAt)
        {
            return AppResult<AdventureDraftState>.Fail(DraftChanged());
        }

        if (DraftEdits.Apply(adventure.DraftMarkdown ?? "", edits[command.Index]) is not { } changed)
        {
            return AppResult<AdventureDraftState>.Fail(new AppError(409, "edit_no_longer_fits",
                "The passage this edit changes isn't in the draft exactly once any more, so it can't be applied. Ask the Loremaster again."));
        }

        return await WriteWithRevisionAsync(adventure, changed, AdventureDraftChange.Edit, command.BasedOnUpdatedAt, ct);
    }

    public async Task<AppResult<AdventureDraftState>> PutInDraftAsync(PutInDraftCommand command, CancellationToken ct)
    {
        var (adventure, answer, error) = await LoadAnswerAsync(
            command.WorldId, command.AdventureId, command.ActingUserRole, command.MessageId, ct);
        if (error is not null)
        {
            return AppResult<AdventureDraftState>.Fail(error);
        }

        if (adventure!.DraftUpdatedAt != command.BasedOnUpdatedAt)
        {
            return AppResult<AdventureDraftState>.Fail(DraftChanged());
        }

        var text = answer!.Content.Trim();
        var markdown = command.Mode == PutInSectionMode.Append && !string.IsNullOrWhiteSpace(adventure.DraftMarkdown)
            ? adventure.DraftMarkdown.TrimEnd() + "\n\n" + text
            : text;
        return await WriteWithRevisionAsync(adventure, markdown, AdventureDraftChange.Put, command.BasedOnUpdatedAt, ct);
    }

    public async Task<AppResult<AdventureView>> SplitAsync(SplitDraftCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventureView>.Fail(NotFound());
        }

        if (string.IsNullOrWhiteSpace(adventure.DraftMarkdown))
        {
            return AppResult<AdventureView>.Fail(NothingToSplit());
        }

        if (adventure.DraftUpdatedAt != command.BasedOnUpdatedAt)
        {
            return AppResult<AdventureView>.Fail(DraftChanged());
        }

        var replace = command.Mode == DraftSplitMode.Replace;
        var staying = replace ? [] : adventure.Sections.ToList();
        var lines = AdventureDraftPlan.LinesOf(adventure.DraftMarkdown);
        if (AdventureDraftPlan.Validate(command.Plan, lines, _options.MaxSections - staying.Count) is { } invalid)
        {
            return AppResult<AdventureView>.Fail(new AppError(400, "invalid_plan", invalid));
        }

        IReadOnlyList<CutSection> cut;
        try
        {
            cut = AdventureDraftCutter.Cut(adventure.DraftMarkdown, command.Plan);
        }
        catch (DraftCutCheckFailedException ex)
        {
            // No plan can fail the check, so this is a bug in the cutter: apply nothing.
            _logger.LogError(ex, "A draft split failed its word check. AdventureId={AdventureId}", adventure.Id);
            return AppResult<AdventureView>.Fail(new AppError(500, "split_check_failed",
                "The split would have changed the draft's words, so nothing was applied. This is a bug; please report it."));
        }

        var now = DateTimeOffset.UtcNow;
        var sections = cut.Select(c => new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = c.Kind,
            Title = c.Title,
            BodyMarkdown = c.BodyMarkdown,
            UpdatedAt = now,
        }).ToList();
        if (PrepBounds.Validate([.. staying, .. sections], _options) is { } tooLarge)
        {
            return AppResult<AdventureView>.Fail(tooLarge);
        }

        // Replace keeps the written sections' text, gathered, as one draft revision, so Undo on
        // the draft brings back everything the split took away.
        var gathered = replace ? AdventureDraftGatherer.Join(adventure.Sections) : "";
        var revision = gathered.Length == 0 ? null : new AdventureDraftRevision
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            BodyMarkdown = gathered,
            Reason = AdventureDraftChange.Split,
            CreatedAt = now,
        };

        if (!await _adventures.TryWriteSplitAsync(
            adventure.Id, sections, replace, revision, _options.MaxRevisionsPerSection, command.BasedOnUpdatedAt, now, ct))
        {
            return AppResult<AdventureView>.Fail(DraftChanged());
        }

        return await _views.GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    public async Task<AppResult<AdventureDraftState>> GatherAsync(GatherDraftCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureDraftState>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventureDraftState>.Fail(NotFound());
        }

        if (adventure.DraftUpdatedAt != command.BasedOnUpdatedAt)
        {
            return AppResult<AdventureDraftState>.Fail(DraftChanged());
        }

        var gathered = AdventureDraftGatherer.Join(adventure.Sections);
        if (gathered.Length == 0)
        {
            return AppResult<AdventureDraftState>.Fail(new AppError(400, "nothing_to_gather",
                "No section has any text yet, so there is nothing to gather."));
        }

        return await WriteWithRevisionAsync(adventure, gathered, AdventureDraftChange.Gather, command.BasedOnUpdatedAt, ct);
    }

    /// <summary>
    /// Writes a change the GM didn't type, keeping the draft before it as a revision for Undo, on
    /// the stamp the change was based on. A first draft has nothing to keep, so no revision.
    /// </summary>
    internal async Task<AppResult<AdventureDraftState>> WriteWithRevisionAsync(
        Adventure adventure, string markdown, AdventureDraftChange reason, DateTimeOffset? basedOn, CancellationToken ct)
    {
        if (markdown.Length > Adventure.MaxDraftChars)
        {
            return AppResult<AdventureDraftState>.Fail(DraftTooLarge(markdown.Length));
        }

        var now = DateTimeOffset.UtcNow;
        var revision = new AdventureDraftRevision
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Reason = reason,
            CreatedAt = now,
        };
        if (!await _adventures.TrySaveDraftAsync(adventure.Id, markdown, revision, _options.MaxRevisionsPerSection, basedOn, now,
            reason.IsLoremasterWriting(), ct))
        {
            return AppResult<AdventureDraftState>.Fail(DraftChanged());
        }

        return AppResult<AdventureDraftState>.Success(
            new(markdown, now, await _adventures.CountDraftRevisionsAsync(adventure.Id, ct)));
    }

    /// <summary>The Adventure and one saved answer from its own conversation, or why not.</summary>
    private async Task<(Adventure? Adventure, ConversationMessage? Answer, AppError? Error)> LoadAnswerAsync(
        Guid worldId, Guid adventureId, WorldRole role, Guid messageId, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return (null, null, GmOnly());
        }

        var adventure = await LoadAsync(worldId, adventureId, ct);
        if (adventure is null)
        {
            return (null, null, NotFound());
        }

        var message = await _conversations.GetMessageAsync(messageId, ct);
        if (message is null
            || message.Role != ConversationRole.Assistant
            || message.Conversation.AdventureId != adventure.Id
            || message.Conversation.WorldId != worldId)
        {
            return (null, null, new AppError(404, "not_found", "That answer isn't in this adventure's conversation."));
        }

        return (adventure, message, null);
    }

    public async Task<AppResult<AdventureView>> SetTableBriefAsync(SetTableBriefCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventureView>.Fail(NotFound());
        }

        var brief = string.IsNullOrWhiteSpace(command.Brief) ? null : command.Brief.Trim();
        if (brief is { Length: > Adventure.MaxTableBriefChars })
        {
            return AppResult<AdventureView>.Fail(new AppError(400, "validation_error",
                $"A table brief can be at most {Adventure.MaxTableBriefChars:N0} characters."));
        }

        adventure.TableBrief = brief;
        adventure.UpdatedAt = DateTimeOffset.UtcNow;
        await _adventures.UpdateAsync(adventure, ct);
        return await _views.GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    public async Task<AppResult<AdventureView>> SetSourceBooksAsync(SetSourceBooksCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventureView>.Fail(NotFound());
        }

        var ids = (command.DocumentIds ?? []).Distinct().ToList();
        if (ids.Count > MaxSourceBooks)
        {
            return AppResult<AdventureView>.Fail(new AppError(400, "too_many_source_books",
                $"An adventure reads at most {MaxSourceBooks} books; that's {ids.Count}."));
        }

        foreach (var id in ids)
        {
            var document = await _documents.GetByIdAsync(id, ct);
            if (document is null || document.WorldId != command.WorldId || document.RemovedAt is not null)
            {
                return AppResult<AdventureView>.Fail(new AppError(404, "document_not_found",
                    "One of those books isn't in this world's Library."));
            }

            if (LibraryCategories.Of(document) != LibraryDocumentCategory.Book)
            {
                return AppResult<AdventureView>.Fail(new AppError(400, "not_a_book",
                    $"\"{document.Title}\" isn't a book. Source books are the Library's books."));
            }
        }

        await _adventures.ReplaceSourceBooksAsync(adventure.Id, ids, ct);
        return await _views.GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    private async Task<Adventure?> LoadAsync(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var adventure = await _adventures.GetWithSectionsAsync(adventureId, ct);
        return adventure is not null && adventure.WorldId == worldId ? adventure : null;
    }

    internal static AppError NothingToSplit() => new(400, "nothing_to_split", "The draft is empty, so there is nothing to split.");

    private static AppError GmOnly() => new(403, "insufficient_role", "Prep is for the world's GM.");

    private static AppError NotFound() => new(404, "not_found", "Adventure not found.");

    internal static AppError DraftChanged() => new(409, "draft_changed",
        "The draft changed since you opened it. Reload it to see the latest version.");

    internal static AppError DraftTooLarge(int length) => new(400, "prep_too_large",
        $"The draft would be {length:N0} characters; a draft holds at most {Adventure.MaxDraftChars:N0}.");
}
