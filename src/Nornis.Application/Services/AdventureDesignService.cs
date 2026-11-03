using Nornis.Application.Errors;
using Nornis.Application.Models;
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

    public AdventureDesignService(
        IAdventureRepository adventures, ILibraryDocumentRepository documents, IAdventureService views)
    {
        _adventures = adventures;
        _documents = documents;
        _views = views;
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
            adventure.Id, markdown, revision: null, maxRevisions: 0, command.BasedOnUpdatedAt, now, ct))
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

    private static AppError GmOnly() => new(403, "insufficient_role", "Prep is for the world's GM.");

    private static AppError NotFound() => new(404, "not_found", "Adventure not found.");

    internal static AppError DraftChanged() => new(409, "draft_changed",
        "The draft changed since you opened it. Reload it to see the latest version.");

    internal static AppError DraftTooLarge(int length) => new(400, "prep_too_large",
        $"The draft would be {length:N0} characters; a draft holds at most {Adventure.MaxDraftChars:N0}.");
}
