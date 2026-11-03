using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <inheritdoc cref="IAdventureService"/>
public class AdventureService : IAdventureService
{
    /// <summary>The most files one import takes. Their text is bounded by <see cref="MaxImportChars"/> too.</summary>
    public const int MaxImportFiles = 50;

    private const int MaxTitleChars = 200;
    private const int MaxPitchChars = 4_000;

    private readonly IAdventureRepository _adventures;
    private readonly ICampaignRepository _campaigns;
    private readonly ISlugResolver _slugs;
    private readonly AdventurePrintDetails _printDetails;
    private readonly PrepOptions _options;

    public AdventureService(
        IAdventureRepository adventures, ICampaignRepository campaigns, ISlugResolver slugs,
        AdventurePrintDetails printDetails, IOptions<PrepOptions> options)
    {
        _adventures = adventures;
        _campaigns = campaigns;
        _slugs = slugs;
        _printDetails = printDetails;
        _options = options.Value;
    }

    /// <summary>
    /// The raw text one import may hand the parser: twice what an Adventure may hold, so a set
    /// of files that would split into a valid Adventure is never refused for its headings, and
    /// a pasted novel is refused before it is parsed.
    /// </summary>
    private int MaxImportChars => _options.MaxAdventureChars * 2;

    public async Task<AppResult<IReadOnlyList<AdventureSummary>>> ListAsync(Guid worldId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<IReadOnlyList<AdventureSummary>>.Fail(GmOnly());
        }

        var list = await _adventures.ListByWorldAsync(worldId, AdventureSkeleton.UnfilledBodies, ct);
        return AppResult<IReadOnlyList<AdventureSummary>>.Success(list);
    }

    public async Task<AppResult<AdventureView>> GetAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(worldId, adventureId, ct);
        return adventure is null
            ? AppResult<AdventureView>.Fail(NotFound())
            : AppResult<AdventureView>.Success(await ViewAsync(adventure, ct));
    }

    public async Task<AppResult<AdventureView>> GetByKeyAsync(Guid worldId, string key, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var id = await _slugs.ResolveAsync<Adventure>(worldId, key, ct);
        return id is { } adventureId
            ? await GetAsync(worldId, adventureId, role, ct)
            : AppResult<AdventureView>.Fail(NotFound());
    }

    public async Task<AppResult<AdventureView>> CreateAsync(CreateAdventureCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var error = ValidateTitle(command.Title) ?? ValidatePitch(command.Pitch)
            ?? await ValidateCampaignAsync(command.WorldId, command.CampaignId, ct);
        if (error is not null)
        {
            return AppResult<AdventureView>.Fail(error);
        }

        var now = DateTimeOffset.UtcNow;
        var adventure = NewAdventure(command.WorldId, command.ActingUserId, command.Title, command.Pitch, command.CampaignId, now);
        adventure.Sections = AdventureSkeleton.Create(adventure.Id, now).ToList();

        await _adventures.CreateAsync(adventure, ct);
        return await GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    public async Task<AppResult<AdventureView>> ImportAsync(ImportAdventureCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        if (command.Files.Count > MaxImportFiles)
        {
            return AppResult<AdventureView>.Fail(TooLarge(
                $"That's {command.Files.Count:N0} files; an import takes at most {MaxImportFiles:N0}."));
        }

        var rawChars = command.Files.Sum(f => (long)(f.Markdown?.Length ?? 0));
        if (rawChars > MaxImportChars)
        {
            return AppResult<AdventureView>.Fail(TooLarge(
                $"The files hold {rawChars:N0} characters; an import takes at most {MaxImportChars:N0}."));
        }

        var campaignError = await ValidateCampaignAsync(command.WorldId, command.CampaignId, ct);
        if (campaignError is not null)
        {
            return AppResult<AdventureView>.Fail(campaignError);
        }

        var now = DateTimeOffset.UtcNow;
        var (imported, importError) = AdventureMarkdownImporter.Import(command.Files, now.Year);
        if (imported is null)
        {
            return AppResult<AdventureView>.Fail(new AppError(400, "validation_error", importError!));
        }

        var adventure = NewAdventure(command.WorldId, command.ActingUserId, imported.Title, pitch: null, command.CampaignId, now);
        adventure.Details = imported.Details;
        adventure.Sections = imported.Sections.Select((s, i) => new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = s.Kind,
            Title = s.Title,
            BodyMarkdown = s.BodyMarkdown,
            SortOrder = i + 1,
            UpdatedAt = now,
        }).ToList();

        var boundError = ValidateBounds(adventure.Sections.ToList());
        if (boundError is not null)
        {
            return AppResult<AdventureView>.Fail(boundError);
        }

        await _adventures.CreateAsync(adventure, ct);
        var view = await GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
        return view.IsSuccess
            ? AppResult<AdventureView>.Success(view.Value! with { IgnoredDetails = imported.IgnoredDetails })
            : view;
    }

    public async Task<AppResult<Adventure>> UpdateAsync(UpdateAdventureCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Adventure>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult<Adventure>.Fail(NotFound());
        }

        var error = ValidateTitle(command.Title) ?? ValidatePitch(command.Pitch)
            ?? await ValidateCampaignAsync(command.WorldId, command.CampaignId, ct);
        if (error is not null)
        {
            return AppResult<Adventure>.Fail(error);
        }

        adventure.Title = command.Title.Trim();
        adventure.Pitch = NormalizePitch(command.Pitch);
        adventure.CampaignId = command.CampaignId;
        adventure.UpdatedAt = DateTimeOffset.UtcNow;
        await _adventures.UpdateAsync(adventure, ct);

        return AppResult<Adventure>.Success(adventure);
    }

    public async Task<AppResult<AdventureView>> UpdateDetailsAsync(UpdateAdventureDetailsCommand command, CancellationToken ct)
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

        var now = DateTimeOffset.UtcNow;
        var details = AdventureDetails.Create(command.GameSystem, command.Edition, command.Levels, command.Year);
        if (details.Validate(now.Year) is { } field)
        {
            return AppResult<AdventureView>.Fail(InvalidDetails(field));
        }

        adventure.Details = details;
        adventure.UpdatedAt = now;
        await _adventures.UpdateAsync(adventure, ct);

        return AppResult<AdventureView>.Success(await ViewAsync(adventure, ct));
    }

    public async Task<AppResult<AdventureSection>> SaveSectionAsync(SaveSectionCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureSection>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        var section = adventure?.Sections.FirstOrDefault(s => s.Id == command.SectionId);
        if (adventure is null || section is null)
        {
            return AppResult<AdventureSection>.Fail(NotFound());
        }

        var titleError = ValidateSectionTitle(command.Title);
        if (titleError is not null)
        {
            return AppResult<AdventureSection>.Fail(titleError);
        }

        section.Kind = command.Kind;
        section.Title = command.Title.Trim();
        section.BodyMarkdown = command.BodyMarkdown ?? "";

        var boundError = ValidateBounds(adventure.Sections.ToList());
        if (boundError is not null)
        {
            return AppResult<AdventureSection>.Fail(boundError);
        }

        var now = DateTimeOffset.UtcNow;
        if (!await _adventures.TryUpdateSectionAsync(section, command.BasedOnUpdatedAt, now, ct))
        {
            return AppResult<AdventureSection>.Fail(SectionChanged());
        }

        section.UpdatedAt = now;
        return AppResult<AdventureSection>.Success(section);
    }

    public async Task<AppResult<AdventureView>> AddSectionAsync(AddSectionCommand command, CancellationToken ct)
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

        if (command.AfterSectionId is { } after && adventure.Sections.All(s => s.Id != after))
        {
            return AppResult<AdventureView>.Fail(NotFound());
        }

        var titleError = ValidateSectionTitle(command.Title);
        if (titleError is not null)
        {
            return AppResult<AdventureView>.Fail(titleError);
        }

        var section = new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = command.Kind,
            Title = command.Title.Trim(),
            BodyMarkdown = command.Kind == AdventureSectionKind.Scene ? AdventureSkeleton.SceneTemplate : "",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var boundError = ValidateBounds([.. adventure.Sections, section]);
        if (boundError is not null)
        {
            return AppResult<AdventureView>.Fail(boundError);
        }

        await _adventures.InsertSectionAsync(section, command.AfterSectionId, ct);
        return await GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    public async Task<AppResult<AdventureView>> DeleteSectionAsync(DeleteSectionCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureView>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null || adventure.Sections.All(s => s.Id != command.SectionId))
        {
            return AppResult<AdventureView>.Fail(NotFound());
        }

        await _adventures.DeleteSectionAsync(adventure.Id, command.SectionId, ct);
        return await GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    public async Task<AppResult<AdventureView>> ReorderAsync(ReorderSectionsCommand command, CancellationToken ct)
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

        // Every section exactly once: an order that leaves one out or names a stranger is a
        // stale client, and guessing where the rest go would be guessing the GM's order.
        var current = adventure.Sections.Select(s => s.Id).ToHashSet();
        var given = command.OrderedSectionIds ?? [];
        if (given.Count != current.Count || given.Distinct().Count() != given.Count || !given.All(current.Contains))
        {
            return AppResult<AdventureView>.Fail(new AppError(400, "validation_error",
                "The order must name every section of this adventure exactly once."));
        }

        await _adventures.ReorderSectionsAsync(adventure.Id, given, ct);
        return await GetAsync(command.WorldId, adventure.Id, command.ActingUserRole, ct);
    }

    public async Task<AppResult> DeleteAsync(DeleteAdventureCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        if (adventure is null)
        {
            return AppResult.Fail(NotFound());
        }

        await _adventures.DeleteAsync(adventure.Id, ct);
        return AppResult.Success();
    }

    public async Task<AppResult<AdventurePrint>> GetPrintAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<AdventurePrint>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(worldId, adventureId, ct);
        if (adventure is null)
        {
            return AppResult<AdventurePrint>.Fail(NotFound());
        }

        var details = await _printDetails.ResolveAsync(adventure, ct);
        return AppResult<AdventurePrint>.Success(new AdventurePrint(
            adventure, AdventurePrintModel.Render(adventure, details), LibraryService.PrintTitle(adventure.Title, DateTimeOffset.UtcNow)));
    }

    public async Task<AppResult<AdventureSection>> UndoAsync(UndoSectionCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<AdventureSection>.Fail(GmOnly());
        }

        var adventure = await LoadAsync(command.WorldId, command.AdventureId, ct);
        var section = adventure?.Sections.FirstOrDefault(s => s.Id == command.SectionId);
        if (section is null)
        {
            return AppResult<AdventureSection>.Fail(NotFound());
        }

        var revision = await _adventures.GetLatestRevisionAsync(section.Id, ct);
        if (revision is null)
        {
            return AppResult<AdventureSection>.Fail(new AppError(409, "nothing_to_undo",
                "This section has no AI change to undo."));
        }

        var now = DateTimeOffset.UtcNow;
        if (!await _adventures.TryRestoreRevisionAsync(revision, section.UpdatedAt, now, ct))
        {
            return AppResult<AdventureSection>.Fail(SectionChanged());
        }

        section.BodyMarkdown = revision.BodyMarkdown;
        section.UpdatedAt = now;
        return AppResult<AdventureSection>.Success(section);
    }

    private async Task<Adventure?> LoadAsync(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var adventure = await _adventures.GetWithSectionsAsync(adventureId, ct);
        return adventure is not null && adventure.WorldId == worldId ? adventure : null;
    }

    private async Task<AdventureView> ViewAsync(Adventure adventure, CancellationToken ct) =>
        new(adventure, await _adventures.CountRevisionsAsync(adventure.Id, ct), await _printDetails.ResolveAsync(adventure, ct));

    private static Adventure NewAdventure(
        Guid worldId, Guid userId, string title, string? pitch, Guid? campaignId, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CampaignId = campaignId,
            Title = title.Trim(),
            Pitch = NormalizePitch(pitch),
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static string? NormalizePitch(string? pitch) =>
        string.IsNullOrWhiteSpace(pitch) ? null : pitch.Trim();

    private AppError? ValidateBounds(IReadOnlyList<AdventureSection> sections) => PrepBounds.Validate(sections, _options);

    private async Task<AppError?> ValidateCampaignAsync(Guid worldId, Guid? campaignId, CancellationToken ct)
    {
        if (campaignId is not { } id)
        {
            return null;
        }

        var campaign = await _campaigns.GetByIdAsync(id, ct);
        return campaign is null || campaign.WorldId != worldId
            ? new AppError(400, "invalid_campaign", "That campaign isn't in this world.")
            : null;
    }

    private static AppError? ValidateTitle(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? new AppError(400, "validation_error", "An adventure needs a title.")
            : title.Trim().Length > MaxTitleChars
                ? new AppError(400, "validation_error", $"A title can be at most {MaxTitleChars:N0} characters.")
                : null;

    private static AppError? ValidateSectionTitle(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? new AppError(400, "validation_error", "A section needs a title.")
            : title.Trim().Length > MaxTitleChars
                ? new AppError(400, "validation_error", $"A section title can be at most {MaxTitleChars:N0} characters.")
                : null;

    private static AppError? ValidatePitch(string? pitch) =>
        pitch is not null && pitch.Trim().Length > MaxPitchChars
            ? new AppError(400, "validation_error", $"A pitch can be at most {MaxPitchChars:N0} characters.")
            : null;

    private static AppError GmOnly() => new(403, "insufficient_role", "Prep is for the world's GM.");

    private static AppError NotFound() => new(404, "not_found", "Adventure not found.");

    private static AppError InvalidDetails(string field) => new(400, "invalid_details", field switch
    {
        nameof(AdventureDetails.GameSystem) => $"The game system can be at most {AdventureDetails.MaxGameSystemLength} characters.",
        nameof(AdventureDetails.Edition) => $"The edition can be at most {AdventureDetails.MaxEditionLength} characters.",
        nameof(AdventureDetails.Levels) => $"The levels can be at most {AdventureDetails.MaxLevelsLength} characters.",
        _ => $"The year must be between {AdventureDetails.MinYear} and {AdventureDetails.MaxYearsAhead} years from now.",
    });

    private static AppError TooLarge(string message) => PrepBounds.TooLarge(message);

    private static AppError SectionChanged() => new(409, "section_changed",
        "This section changed since you opened it. Reload it to see the latest version.");
}
