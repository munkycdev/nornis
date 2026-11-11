using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Art;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Services;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Prep.Covers;

/// <summary>
/// An Adventure's cover art (feature 38): the Loremaster's art brief, generated options on the
/// world creator's own image key, the GM's pick or upload, and the layout. GM-only throughout.
///
/// "The model paints, Nornis sets the type": nothing here puts words on an image. The rights
/// record follows the art: choosing a generated option records that AI made art, in the same
/// save, and an upload records nothing.
/// </summary>
public class AdventureCoverService
{
    /// <summary>The ledger's model name for a generation that failed before a model was bound.</summary>
    public const string UnboundModel = "cover-art";

    /// <summary>A cover this many pixels or more on its short side prints sharp on a page.</summary>
    public const int SharpShortSide = 1_000;

    private const int EstimatedCharsPerToken = 4;

    private readonly IAdventureRepository _adventures;
    private readonly AdventurePrintDetails _printDetails;
    private readonly IAdventureDraftingAiClient _ai;
    private readonly IImageArtGenerator _generator;
    private readonly IBlobStorageService _blobs;
    private readonly IAiBudgetGuard _budget;
    private readonly IAiUsageRecorder _usage;
    private readonly PrepOptions _options;
    private readonly LoremasterOptions _loremaster;
    private readonly TimeProvider _time;
    private readonly ILogger<AdventureCoverService> _logger;

    public AdventureCoverService(
        IAdventureRepository adventures,
        AdventurePrintDetails printDetails,
        IAdventureDraftingAiClient ai,
        IImageArtGenerator generator,
        IBlobStorageService blobs,
        IAiBudgetGuard budget,
        IAiUsageRecorder usage,
        IOptions<PrepOptions> options,
        IOptions<LoremasterOptions> loremaster,
        TimeProvider time,
        ILogger<AdventureCoverService> logger)
    {
        _adventures = adventures;
        _printDetails = printDetails;
        _ai = ai;
        _generator = generator;
        _blobs = blobs;
        _budget = budget;
        _usage = usage;
        _options = options.Value;
        _loremaster = loremaster.Value;
        _time = time;
        _logger = logger;
    }

    private string BriefModel => string.IsNullOrWhiteSpace(_options.Deployment) ? _loremaster.AiModel : _options.Deployment;

    public async Task<AppResult<CoverState>> GetAsync(Guid worldId, Guid adventureId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(worldId, adventureId, role, ct);
        return error is not null ? Fail(error) : AppResult<CoverState>.Success(await StateAsync(adventure!, actingUserId, ct));
    }

    /// <summary>
    /// Asks the Loremaster for an art brief from the title, pitch, Overview and game system only,
    /// and saves it as the GM's working brief. Text only: the brief is never in the book, so it
    /// records no AI use.
    /// </summary>
    public async Task<AppResult<CoverState>> WriteBriefAsync(WriteCoverBriefCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        var details = await _printDetails.ResolveAsync(adventure!, ct);
        var request = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(command.WorldId, command.ActingUserId),
            SystemPrompt = CoverArtPrompt.BriefSystemPrompt,
            UserMessage = CoverArtPrompt.BuildBriefMessage(adventure!, details.SystemLine()),
            Model = BriefModel,
            TimeoutSeconds = _options.DraftTimeoutSeconds,
        };

        var (brief, failure) = await AskForBriefAsync(command, request, ct);
        if (failure is not null)
        {
            return Fail(failure);
        }

        await _adventures.SaveCoverWorkAsync(adventure!.Id, brief, [], ct);
        adventure.CoverBrief = brief;
        return AppResult<CoverState>.Success(await StateAsync(adventure, command.ActingUserId, ct));
    }

    /// <summary>The GM's own edit of the brief, kept so a reload doesn't lose it.</summary>
    public async Task<AppResult<CoverState>> SaveBriefAsync(SaveCoverBriefCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        var brief = string.IsNullOrWhiteSpace(command.Brief) ? null : command.Brief.Trim();
        if (brief is { Length: > AdventureCover.MaxBriefChars })
        {
            return Fail(new AppError(400, "invalid_brief", CoverArtPrompt.Invalid(brief)!));
        }

        await _adventures.SaveCoverWorkAsync(adventure!.Id, brief, [], ct);
        adventure.CoverBrief = brief;
        return AppResult<CoverState>.Success(await StateAsync(adventure, command.ActingUserId, ct));
    }

    /// <summary>
    /// Makes up to <see cref="PrepOptions.CoverOptionsPerGeneration"/> options from the brief.
    /// The images are counted against the cap before the call, so two clicks at once can't pass
    /// it, and given back when the call fails. A generation is spent from the Prep pool.
    /// </summary>
    public async Task<AppResult<CoverState>> GenerateAsync(GenerateCoverCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (CoverArtPrompt.Invalid(command.Brief) is { } invalid)
        {
            return Fail(new AppError(400, "invalid_brief", invalid));
        }

        var count = Math.Min(_options.CoverOptionsPerGeneration, _options.MaxCoverImagesPerAdventure - adventure!.CoverImagesGenerated);
        if (count <= 0)
        {
            return Fail(CapReached());
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        if (!await _adventures.TryCountCoverImagesAsync(adventure.Id, count, _options.MaxCoverImagesPerAdventure, ct))
        {
            return Fail(CapReached());
        }

        var brief = command.Brief.Trim();
        var scope = AiCallScope.ForWorld(command.WorldId, command.ActingUserId);
        var stopwatch = Stopwatch.StartNew();
        ImageArtGeneration generation;
        try
        {
            generation = await _generator.GenerateAsync(
                scope, CoverArtPrompt.ImagePrompt(brief), ImageShape.Portrait, count, _options.CoverTimeoutSeconds, nameof(AiOperationType.CoverArt), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The GM left. Whether the provider finished and charged is unknown, so the images
            // stay counted: the cap errs toward spending less.
            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.CoverArt, null, succeeded: true, "Cancelled",
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await _adventures.ReleaseCoverImagesAsync(adventure.Id, count, CancellationToken.None);
            var (code, appError) = Classify(ex);
            if (ex is not AiRefusalException)
            {
                _logger.LogWarning(ex, "A cover generation failed. AdventureId={AdventureId}", adventure.Id);
            }

            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.CoverArt, null, succeeded: false, code,
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            return Fail(appError);
        }

        await _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.CoverArt,
            generation.Usage with { DurationMs = (int)stopwatch.Elapsed.TotalMilliseconds },
            succeeded: true, errorCode: null, ct: CancellationToken.None);

        if (generation.Images.Count < count)
        {
            await _adventures.ReleaseCoverImagesAsync(adventure.Id, count - generation.Images.Count, CancellationToken.None);
        }

        var ids = new List<Guid>();
        foreach (var image in generation.Images.Take(count))
        {
            var id = Guid.NewGuid();
            using var stream = new MemoryStream(image.Png, writable: false);
            await _blobs.UploadAsync(CoverBlobs.OptionPath(adventure.WorldId, adventure.Id, id), stream, "image/png", CancellationToken.None);
            ids.Add(id);
        }

        await _adventures.SaveCoverWorkAsync(adventure.Id, brief, ids, CancellationToken.None);
        _logger.LogInformation("cover_generated {AdventureId} images={Images}", adventure.Id, ids.Count);

        return AppResult<CoverState>.Success(await StateAsync(await ReloadAsync(adventure.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>
    /// Refines one generated option (feature 45): its picture and the current brief go to the
    /// refining model as an edit, and the result is added as a new option, shown first. Nothing
    /// else changes until the GM chooses. One image against the same cap, counted before the call
    /// and given back when it fails or delivers nothing; spent from the Prep pool.
    /// </summary>
    public async Task<AppResult<CoverState>> RefineAsync(RefineCoverCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (!adventure!.CoverOptionIds.Contains(command.OptionId))
        {
            return Fail(OptionGone());
        }

        if (adventure.CoverImagesGenerated >= _options.MaxCoverImagesPerAdventure)
        {
            return Fail(CapReached());
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        if (!await _adventures.TryCountCoverImagesAsync(adventure.Id, 1, _options.MaxCoverImagesPerAdventure, ct))
        {
            return Fail(CapReached());
        }

        if (await ReadAsync(CoverBlobs.OptionPath(adventure.WorldId, adventure.Id, command.OptionId), ct) is not { } source)
        {
            await _adventures.ReleaseCoverImagesAsync(adventure.Id, 1, CancellationToken.None);
            return Fail(OptionGone());
        }

        var scope = AiCallScope.ForWorld(command.WorldId, command.ActingUserId);
        var prompt = ArtRefinePrompt.For(adventure.CoverBrief, CoverArtPrompt.ImageRules);
        var stopwatch = Stopwatch.StartNew();
        ImageArtGeneration refined;
        try
        {
            refined = await _generator.RefineAsync(
                scope, source, prompt, ImageShape.Portrait, _options.CoverTimeoutSeconds, nameof(AiOperationType.CoverArtRefinement), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // As a generation: whether the provider charged is unknown, so the image stays counted.
            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.CoverArtRefinement, null, succeeded: true, "Cancelled",
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await _adventures.ReleaseCoverImagesAsync(adventure.Id, 1, CancellationToken.None);
            var (code, appError) = Classify(ex);
            if (ex is not AiRefusalException)
            {
                _logger.LogWarning(ex, "A cover refine failed. AdventureId={AdventureId}", adventure.Id);
            }

            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.CoverArtRefinement, null, succeeded: false, code,
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            return Fail(appError);
        }

        await _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.CoverArtRefinement,
            refined.Usage with { DurationMs = (int)stopwatch.Elapsed.TotalMilliseconds },
            succeeded: true, errorCode: null, ct: CancellationToken.None);

        if (refined.Images.FirstOrDefault() is not { } image)
        {
            await _adventures.ReleaseCoverImagesAsync(adventure.Id, 1, CancellationToken.None);
            return Fail(new AppError(503, "service_unavailable", "The image model sent nothing back. Nothing was counted. Please try again."));
        }

        var id = Guid.NewGuid();
        using (var stream = new MemoryStream(image.Png, writable: false))
        {
            await _blobs.UploadAsync(CoverBlobs.OptionPath(adventure.WorldId, adventure.Id, id), stream, "image/png", CancellationToken.None);
        }

        // The brief is left as it is: appending the option is the only change.
        await _adventures.SaveCoverWorkAsync(adventure.Id, adventure.CoverBrief, [id], CancellationToken.None);
        _logger.LogInformation("cover_refined {AdventureId} from={OptionId}", adventure.Id, command.OptionId);

        return AppResult<CoverState>.Success(await StateAsync(await ReloadAsync(adventure.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>Makes one generated option the cover, and records in the same save that AI made art.</summary>
    public async Task<AppResult<CoverState>> ChooseAsync(ChooseCoverCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (!adventure!.CoverOptionIds.Contains(command.OptionId))
        {
            return Fail(OptionGone());
        }

        var cover = new AdventureCover
        {
            BlobPath = CoverBlobs.OptionPath(adventure.WorldId, adventure.Id, command.OptionId),
            ContentType = "image/png",
            Source = CoverSource.Generated,
            Brief = adventure.CoverBrief,
            Layout = adventure.Cover?.Layout ?? CoverLayout.FullBleed,
            UpdatedAt = _time.GetUtcNow(),
        };
        await ReplaceCoverAsync(adventure, cover, generated: true, ct);

        return AppResult<CoverState>.Success(await StateAsync(await ReloadAsync(adventure.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>
    /// The GM's own art: a PNG or JPEG read from its bytes, at most
    /// <see cref="PrepOptions.MaxCoverUploadBytes"/>. Small art is taken with a warning, since the
    /// GM may know it's all they have. Records no AI use.
    /// </summary>
    public async Task<AppResult<CoverState>> UploadAsync(UploadCoverCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (command.Bytes.Length == 0)
        {
            return Fail(new AppError(400, "invalid_image", "Choose a PNG or JPEG image."));
        }

        if (command.Bytes.Length > _options.MaxCoverUploadBytes)
        {
            return Fail(new AppError(413, "image_too_large",
                $"A cover can be at most {_options.MaxCoverUploadBytes / (1024 * 1024)} MB."));
        }

        if (ImageFile.Read(command.Bytes) is not { } info)
        {
            return Fail(new AppError(400, "invalid_image", "A cover must be a PNG or JPEG image."));
        }

        var path = CoverBlobs.UploadPath(adventure!.WorldId, adventure.Id, Guid.NewGuid(), info.Extension);
        using (var stream = new MemoryStream(command.Bytes, writable: false))
        {
            await _blobs.UploadAsync(path, stream, info.ContentType, ct);
        }

        var cover = new AdventureCover
        {
            BlobPath = path,
            ContentType = info.ContentType,
            Source = CoverSource.Uploaded,
            Layout = adventure.Cover?.Layout ?? CoverLayout.FullBleed,
            UpdatedAt = _time.GetUtcNow(),
        };
        await ReplaceCoverAsync(adventure, cover, generated: false, ct);

        var warning = Math.Min(info.Width, info.Height) < SharpShortSide
            ? $"This image is {info.Width} × {info.Height} pixels, so it may print soft. Covers print sharp at {SharpShortSide} pixels or more on the short side."
            : null;
        var state = await StateAsync(await ReloadAsync(adventure.Id, ct), command.ActingUserId, ct);
        return AppResult<CoverState>.Success(state with { Warning = warning });
    }

    /// <summary>Takes the art off the cover. A generated option stays to pick again; an upload is deleted.</summary>
    public async Task<AppResult<CoverState>> RemoveAsync(RemoveCoverCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (adventure!.Cover is not null)
        {
            await ReplaceCoverAsync(adventure, null, generated: false, ct);
        }

        return AppResult<CoverState>.Success(await StateAsync(await ReloadAsync(adventure.Id, ct), command.ActingUserId, ct));
    }

    public async Task<AppResult<CoverState>> SetLayoutAsync(SetCoverLayoutCommand command, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(command.WorldId, command.AdventureId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (adventure!.Cover is not { } cover)
        {
            return Fail(new AppError(409, "no_cover", "Add cover art first, then choose its layout."));
        }

        if (cover.Layout != command.Layout)
        {
            await _adventures.SetCoverAsync(adventure.Id, cover with { Layout = command.Layout }, generated: false, _time.GetUtcNow(), ct);
        }

        return AppResult<CoverState>.Success(await StateAsync(await ReloadAsync(adventure.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>The cover's image, for the Web's proxy.</summary>
    public async Task<AppResult<CoverImage>> OpenCoverAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(worldId, adventureId, role, ct);
        if (error is not null)
        {
            return AppResult<CoverImage>.Fail(error);
        }

        return adventure!.Cover is { } cover && await ReadAsync(cover.BlobPath, ct) is { } bytes
            ? AppResult<CoverImage>.Success(new CoverImage(bytes, cover.ContentType))
            : AppResult<CoverImage>.Fail(NotFound());
    }

    /// <summary>A generated option's image, for the Web's proxy.</summary>
    public async Task<AppResult<CoverImage>> OpenOptionAsync(Guid worldId, Guid adventureId, Guid optionId, WorldRole role, CancellationToken ct)
    {
        var (adventure, error) = await LoadAsync(worldId, adventureId, role, ct);
        if (error is not null)
        {
            return AppResult<CoverImage>.Fail(error);
        }

        return adventure!.CoverOptionIds.Contains(optionId)
            && await ReadAsync(CoverBlobs.OptionPath(worldId, adventureId, optionId), ct) is { } bytes
            ? AppResult<CoverImage>.Success(new CoverImage(bytes, "image/png"))
            : AppResult<CoverImage>.Fail(NotFound());
    }

    private Task<byte[]?> ReadAsync(string blobPath, CancellationToken ct) => _blobs.ReadAllBytesOrNullAsync(blobPath, ct);

    /// <summary>Sets the cover, then deletes the upload it replaced; a generated option is kept to pick again.</summary>
    private async Task ReplaceCoverAsync(Adventure adventure, AdventureCover? cover, bool generated, CancellationToken ct)
    {
        var previous = adventure.Cover;
        await _adventures.SetCoverAsync(adventure.Id, cover, generated, _time.GetUtcNow(), ct);
        if (previous is not null && CoverBlobs.IsUpload(previous.BlobPath) && previous.BlobPath != cover?.BlobPath)
        {
            await _blobs.DeleteBlobAsync(previous.BlobPath, CancellationToken.None);
        }
    }

    private async Task<CoverState> StateAsync(Adventure adventure, Guid actingUserId, CancellationToken ct)
    {
        var cap = _options.MaxCoverImagesPerAdventure;
        var hasKey = await _generator.CanGenerateAsync(AiCallScope.ForWorld(adventure.WorldId, actingUserId), ct);
        var blocker = !hasKey ? NoKeyHint
            : adventure.CoverImagesGenerated >= cap ? $"This adventure has used its {cap} generated images. You can still upload your own art."
            : null;
        var chosen = adventure.Cover is { Source: CoverSource.Generated } cover
            ? adventure.CoverOptionIds.FirstOrDefault(id => CoverBlobs.OptionPath(adventure.WorldId, adventure.Id, id) == cover.BlobPath)
            : default;

        return new CoverState(
            adventure.Cover,
            adventure.CoverBrief,
            [.. Enumerable.Reverse(adventure.CoverOptionIds)],
            chosen == Guid.Empty ? null : chosen,
            adventure.CoverImagesGenerated,
            cap,
            blocker is null,
            blocker);
    }

    /// <summary>What the Cover card says when no key can make images.</summary>
    public const string NoKeyHint = "Add an OpenAI key to generate covers. You can still upload your own art.";

    private async Task<(Adventure? Adventure, AppError? Error)> LoadAsync(
        Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return (null, new AppError(403, "insufficient_role", "Prep is for the world's GM."));
        }

        var adventure = await _adventures.GetWithSectionsAsync(adventureId, ct);
        return adventure is null || adventure.WorldId != worldId ? (null, NotFound()) : (adventure, null);
    }

    private async Task<Adventure> ReloadAsync(Guid adventureId, CancellationToken ct) =>
        await _adventures.GetWithSectionsAsync(adventureId, ct)
        ?? throw new InvalidOperationException($"Adventure '{adventureId}' vanished during a cover change.");

    /// <summary>The brief call, read whole. A failure is recorded and answered as an error; nothing is saved.</summary>
    private async Task<(string Brief, AppError? Failure)> AskForBriefAsync(
        WriteCoverBriefCommand command, AiPromptRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();
        AiUsage? usage = null;
        string? code = null;
        AppError? failure = null;
        try
        {
            await foreach (var chunk in _ai.StreamAsync(request, ct))
            {
                usage = chunk.Usage ?? usage;
                text.Append(chunk.Text);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await RecordBriefAsync(command, request, text, usage, stopwatch.Elapsed, succeeded: true, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            (code, failure) = Classify(ex);
            if (ex is not AiRefusalException)
            {
                _logger.LogWarning(ex, "A cover brief failed. AdventureId={AdventureId}", command.AdventureId);
            }
        }

        var brief = text.ToString().Trim();
        if (failure is null && CoverArtPrompt.Invalid(brief) is not null)
        {
            (code, failure) = ("Refused", new AppError(503, "service_unavailable",
                "The Loremaster couldn't write a usable brief just now. Try again, or write your own."));
        }

        await RecordBriefAsync(command, request, text, usage, stopwatch.Elapsed, code is null or "Refused", code);
        return (brief, failure);
    }

    private Task RecordBriefAsync(
        WriteCoverBriefCommand command, AiPromptRequest request, StringBuilder text, AiUsage? reported,
        TimeSpan elapsed, bool succeeded, string? errorCode)
    {
        var usage = (reported ?? Estimate(request, text)) with { DurationMs = (int)elapsed.TotalMilliseconds };
        return _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.CoverArtBrief, usage,
            succeeded, errorCode, fallbackModel: request.Model, ct: CancellationToken.None);
    }

    private static AiUsage Estimate(AiPromptRequest request, StringBuilder text)
    {
        var input = text.Length > 0 ? (request.SystemPrompt.Length + request.UserMessage.Length) / EstimatedCharsPerToken : 0;
        var output = text.Length / EstimatedCharsPerToken;
        return new AiUsage { Model = request.Model, InputTokens = input, OutputTokens = output, TotalTokens = input + output, DurationMs = 0 };
    }

    private static (string Code, AppError Error) Classify(Exception ex) => ImageArtFailures.Classify(ex, Words);

    /// <summary>The Cover card's words for a failed generation (or brief).</summary>
    private static readonly ImageArtWords Words = new(
        RefusedCode: "cover_refused",
        Refused: "The image model's safety check turned this brief down. Try rewording it, or upload your own art.",
        RejectedCode: "cover_rejected",
        RejectedTail: "Nothing was charged against this adventure's images.",
        Unavailable: "Cover art is temporarily unavailable. Nothing was changed. Please try again.");

    private AppError CapReached() => new(409, "cover_cap",
        $"This adventure has used its {_options.MaxCoverImagesPerAdventure} generated images. You can still upload your own art.");

    private static AppError NotFound() => new(404, "not_found", "Adventure not found.");

    private static AppError OptionGone() => new(404, "not_found", "That cover option is gone. Generate again or pick another.");

    private static AppResult<CoverState> Fail(AppError error) => AppResult<CoverState>.Fail(error);
}
