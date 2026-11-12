using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <inheritdoc cref="IAdventureExportService"/>
public class AdventureExportService : IAdventureExportService
{
    private readonly IAdventureRepository _adventures;
    private readonly ISourceRepository _sources;
    private readonly ISourceService _sourceService;
    private readonly IWorldRepository _worlds;
    private readonly ILogger<AdventureExportService> _logger;

    public AdventureExportService(
        IAdventureRepository adventures, ISourceRepository sources, ISourceService sourceService,
        IWorldRepository worlds, ILogger<AdventureExportService> logger)
    {
        _adventures = adventures;
        _worlds = worlds;
        _sources = sources;
        _sourceService = sourceService;
        _logger = logger;
    }

    public async Task<AppResult<Source>> ExportAsync(ExportAdventureCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Prep is for the world's GM."));
        }

        var adventure = await _adventures.GetWithSectionsAsync(command.AdventureId, ct);
        if (adventure is null || adventure.WorldId != command.WorldId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Adventure not found."));
        }

        var sections = AdventureExportComposer.Select(adventure, command.ChapterSectionId);
        if (sections is null)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "That chapter isn't in this adventure."));
        }

        if (!AdventureExportComposer.HasContent(sections))
        {
            return AppResult<Source>.Fail(new AppError(400, "nothing_to_export",
                "Nothing here is written yet, so there is nothing for Nornis to learn from it."));
        }

        var chapterTitle = command.ChapterSectionId is null ? null : sections[0].Title;
        var now = DateTimeOffset.UtcNow;
        var world = await _worlds.GetByIdAsync(adventure.WorldId, ct);
        var system = PrintDetails.SystemOf(adventure.Details, world?.GameSystem);
        var body = AdventureExportComposer.BuildBody(adventure.Title, chapterTitle, sections, system, now);
        if (body.Length > SourceService.MaxBodyChars)
        {
            // Block lines can be longer than their fences; without them the body is within the
            // bound PrepOptionsTests pins, so the export still goes through, a block as its lines.
            _logger.LogWarning("Adventure {AdventureId} export is {Chars} characters with block lines; filing blocks as their lines.",
                adventure.Id, body.Length);
            body = AdventureExportComposer.BuildBody(adventure.Title, chapterTitle, sections, system, now, blockLines: false);
        }

        // PrepOptions keeps a whole Adventure within this, and PrepOptionsTests pins that it
        // does; this only fires if the bounds drift apart.
        if (body.Length > SourceService.MaxBodyChars)
        {
            return AppResult<Source>.Fail(new AppError(400, "export_too_large",
                $"The export would be {body.Length:N0} characters; a source holds at most {SourceService.MaxBodyChars:N0}. "
                + "Export one chapter at a time."));
        }

        var source = await _sources.CreateAsync(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            CampaignId = adventure.CampaignId,
            Type = SourceType.AdventureExport,
            Title = AdventureExportComposer.BuildTitle(adventure.Title, chapterTitle),
            Body = body,
            CreatedAt = now,
            CreatedByUserId = command.ActingUserId,
            // Prep is drafted with the world's Hidden truth: the export is the GM's, always.
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Draft,
            ExtractionEnabled = true,
            AdventureId = adventure.Id,
        }, ct);

        // The one path every source takes to the queue, status ordering and enqueue-failure
        // revert included, as a Library excerpt does.
        var ready = await _sourceService.MarkReadyAsync(
            new MarkSourceReadyCommand(source.Id, command.WorldId, command.ActingUserId, command.ActingUserRole), ct);
        if (!ready.IsSuccess)
        {
            return AppResult<Source>.Fail(ready.Error!);
        }

        // The chapter is logged through its Guid, never the nullable that came off the request:
        // a Guid can't carry a line break, and the code scanner only trusts the plain type.
        var chapter = command.ChapterSectionId is { } chapterId ? chapterId.ToString() : "whole";
        _logger.LogInformation(
            "Adventure exported. WorldId={WorldId}, AdventureId={AdventureId}, Chapter={ChapterId}, Sections={Sections}, SourceId={SourceId}",
            command.WorldId, adventure.Id, chapter, sections.Count, source.Id);

        return AppResult<Source>.Success(ready.Value!);
    }
}
