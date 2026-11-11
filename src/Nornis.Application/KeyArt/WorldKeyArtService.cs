using System.Diagnostics;
using System.Globalization;
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

namespace Nornis.Application.KeyArt;

/// <summary>
/// A world's key art (feature 42): the GM uploads a picture or generates two options from a brief
/// on the world creator's own image key, chooses one, anchors it, or removes it. Every write is the
/// GM's; every member sees the art, and so does the public site when public access is on.
///
/// Storage stays bounded: a world holds the chosen art and one generation's options at most.
/// Generating again deletes the options not chosen; replacing or removing the art deletes its blob
/// unless it is still one of the options on offer.
/// </summary>
public class WorldKeyArtService
{
    /// <summary>The ledger's model name for a generation that failed before a model was bound.</summary>
    public const string UnboundModel = "key-art";

    /// <summary>
    /// The most options a world keeps on offer (feature 45). Refines add to the options without a
    /// generation clearing them, and the monthly cap resets, so without this bound a world could
    /// refine month after month past what the options column holds (1,000 characters, about 25 ids).
    /// </summary>
    public const int MaxOptionsOnOffer = 8;

    /// <summary>What the Key art card says when no key can make images.</summary>
    public const string NoKeyHint = "Add an OpenAI key to generate key art. You can still upload your own.";

    private static readonly ImageArtWords Words = new(
        RefusedCode: "key_art_refused",
        Refused: "The image model's safety check turned this brief down. Try rewording it, or upload your own art.",
        RejectedCode: "key_art_rejected",
        RejectedTail: "Nothing was counted against this month's images.",
        Unavailable: "Key art is temporarily unavailable. Nothing was changed. Please try again.");

    private readonly IWorldRepository _worlds;
    private readonly IWorldKeyArtRepository _keyArt;
    private readonly IImageArtGenerator _generator;
    private readonly IBlobStorageService _blobs;
    private readonly IAiBudgetGuard _budget;
    private readonly IAiUsageRecorder _usage;
    private readonly KeyArtOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<WorldKeyArtService> _logger;

    public WorldKeyArtService(
        IWorldRepository worlds,
        IWorldKeyArtRepository keyArt,
        IImageArtGenerator generator,
        IBlobStorageService blobs,
        IAiBudgetGuard budget,
        IAiUsageRecorder usage,
        IOptions<KeyArtOptions> options,
        TimeProvider time,
        ILogger<WorldKeyArtService> logger)
    {
        _worlds = worlds;
        _keyArt = keyArt;
        _generator = generator;
        _blobs = blobs;
        _budget = budget;
        _usage = usage;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>The calendar month (UTC) the cap counts: yyyy-MM.</summary>
    private string ThisMonth => _time.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public async Task<AppResult<KeyArtState>> GetAsync(Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(worldId, role, ct);
        return error is not null ? Fail(error) : AppResult<KeyArtState>.Success(await StateAsync(world!, actingUserId, ct));
    }

    /// <summary>The GM's own edit of the brief, kept so a reload doesn't lose it.</summary>
    public async Task<AppResult<KeyArtState>> SaveBriefAsync(SaveKeyArtBriefCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        var brief = string.IsNullOrWhiteSpace(command.Brief) ? null : command.Brief.Trim();
        if (brief is { Length: > WorldKeyArt.MaxBriefChars })
        {
            return Fail(new AppError(400, "invalid_brief", KeyArtPrompt.Invalid(brief)!));
        }

        await _keyArt.SaveKeyArtWorkAsync(world!.Id, brief, world.KeyArtOptionIds, ct);
        return AppResult<KeyArtState>.Success(await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>
    /// Makes up to <see cref="KeyArtOptions.OptionsPerGeneration"/> landscape options from the GM's
    /// brief. The images are counted against the month's cap before the call, so two clicks at once
    /// can't pass it, and given back when the call fails or delivers fewer. Spent from the Prep pool.
    /// </summary>
    public async Task<AppResult<KeyArtState>> GenerateAsync(GenerateKeyArtCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (KeyArtPrompt.Invalid(command.Brief) is { } invalid)
        {
            return Fail(new AppError(400, "invalid_brief", invalid));
        }

        var month = ThisMonth;
        var count = Math.Min(_options.OptionsPerGeneration, _options.MaxImagesPerMonth - ImagesIn(world!, month));
        if (count <= 0)
        {
            return Fail(CapReached());
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        if (!await _keyArt.TryCountKeyArtImagesAsync(world!.Id, month, count, _options.MaxImagesPerMonth, ct))
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
                scope, KeyArtPrompt.ImagePrompt(brief), ImageShape.Landscape, count, _options.TimeoutSeconds,
                nameof(AiOperationType.KeyArt), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The GM left. Whether the provider finished and charged is unknown, so the images
            // stay counted: the cap errs toward spending less.
            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.KeyArt, null, succeeded: true, "Cancelled",
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await _keyArt.ReleaseKeyArtImagesAsync(world.Id, month, count, CancellationToken.None);
            var (code, appError) = ImageArtFailures.Classify(ex, Words);
            if (ex is not AiRefusalException)
            {
                _logger.LogWarning(ex, "A key-art generation failed. WorldId={WorldId}", world.Id);
            }

            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.KeyArt, null, succeeded: false, code,
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            return Fail(appError);
        }

        await _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.KeyArt,
            generation.Usage with { DurationMs = (int)stopwatch.Elapsed.TotalMilliseconds },
            succeeded: true, errorCode: null, ct: CancellationToken.None);

        if (generation.Images.Count < count)
        {
            await _keyArt.ReleaseKeyArtImagesAsync(world.Id, month, count - generation.Images.Count, CancellationToken.None);
        }

        var ids = new List<Guid>();
        foreach (var image in generation.Images.Take(count))
        {
            var id = Guid.NewGuid();
            using var stream = new MemoryStream(image.Png, writable: false);
            await _blobs.UploadAsync(KeyArtBlobs.OptionPath(world.Id, id), stream, "image/png", CancellationToken.None);
            ids.Add(id);
        }

        // A delivery of nothing keeps the options on offer; otherwise the new ones replace them,
        // and the old ones go, except the one the art is (it lives on as the chosen picture).
        // The old list is copied first: the write may update the very instance it was read from.
        var previousOptions = world.KeyArtOptionIds.ToList();
        if (ids.Count > 0)
        {
            await _keyArt.SaveKeyArtWorkAsync(world.Id, brief, ids, CancellationToken.None);
            foreach (var old in previousOptions)
            {
                var path = KeyArtBlobs.OptionPath(world.Id, old);
                if (path != world.KeyArt?.BlobPath)
                {
                    await _blobs.DeleteBlobAsync(path, CancellationToken.None);
                }
            }
        }
        else
        {
            await _keyArt.SaveKeyArtWorkAsync(world.Id, brief, previousOptions, CancellationToken.None);
        }

        _logger.LogInformation("key_art_generated {WorldId} images={Images}", world.Id, ids.Count);
        return AppResult<KeyArtState>.Success(await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>
    /// Refines one option on offer (feature 45): its picture and the current brief go to the
    /// refining model as an edit, and the result joins the options, first. The art doesn't change
    /// until the GM chooses. One image against the month's cap, counted before the call and given
    /// back when it fails or delivers nothing; spent from the Prep pool. The next generation clears
    /// refined options with the rest, so storage stays bounded by the cap.
    /// </summary>
    public async Task<AppResult<KeyArtState>> RefineAsync(RefineKeyArtCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (!world!.KeyArtOptionIds.Contains(command.OptionId))
        {
            return Fail(OptionGone());
        }

        if (world.KeyArtOptionIds.Count >= MaxOptionsOnOffer)
        {
            return Fail(new AppError(409, "key_art_options_full",
                $"There are {world.KeyArtOptionIds.Count} options on offer, the most a world keeps. Choose one, or generate again to start fresh."));
        }

        var month = ThisMonth;
        if (ImagesIn(world, month) >= _options.MaxImagesPerMonth)
        {
            return Fail(CapReached());
        }

        var budgetError = await _budget.CheckPrepAsync(command.WorldId, ct);
        if (budgetError is not null)
        {
            return Fail(budgetError);
        }

        if (!await _keyArt.TryCountKeyArtImagesAsync(world.Id, month, 1, _options.MaxImagesPerMonth, ct))
        {
            return Fail(CapReached());
        }

        if (await _blobs.ReadAllBytesOrNullAsync(KeyArtBlobs.OptionPath(world.Id, command.OptionId), ct) is not { } source)
        {
            await _keyArt.ReleaseKeyArtImagesAsync(world.Id, month, 1, CancellationToken.None);
            return Fail(OptionGone());
        }

        var scope = AiCallScope.ForWorld(command.WorldId, command.ActingUserId);
        var prompt = ArtRefinePrompt.For(world.KeyArtBrief, KeyArtPrompt.ImageRules);
        var stopwatch = Stopwatch.StartNew();
        ImageArtGeneration refined;
        try
        {
            refined = await _generator.RefineAsync(
                scope, source, prompt, ImageShape.Landscape, _options.TimeoutSeconds, nameof(AiOperationType.KeyArtRefinement), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // As a generation: whether the provider charged is unknown, so the image stays counted.
            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.KeyArtRefinement, null, succeeded: true, "Cancelled",
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await _keyArt.ReleaseKeyArtImagesAsync(world.Id, month, 1, CancellationToken.None);
            var (code, appError) = ImageArtFailures.Classify(ex, Words);
            if (ex is not AiRefusalException)
            {
                _logger.LogWarning(ex, "A key-art refine failed. WorldId={WorldId}", world.Id);
            }

            await _usage.RecordAsync(
                command.WorldId, command.ActingUserId, AiOperationType.KeyArtRefinement, null, succeeded: false, code,
                fallbackModel: UnboundModel, ct: CancellationToken.None);
            return Fail(appError);
        }

        await _usage.RecordAsync(
            command.WorldId, command.ActingUserId, AiOperationType.KeyArtRefinement,
            refined.Usage with { DurationMs = (int)stopwatch.Elapsed.TotalMilliseconds },
            succeeded: true, errorCode: null, ct: CancellationToken.None);

        if (refined.Images.FirstOrDefault() is not { } image)
        {
            await _keyArt.ReleaseKeyArtImagesAsync(world.Id, month, 1, CancellationToken.None);
            return Fail(new AppError(503, "service_unavailable", "The image model sent nothing back. Nothing was counted. Please try again."));
        }

        var id = Guid.NewGuid();
        using (var stream = new MemoryStream(image.Png, writable: false))
        {
            await _blobs.UploadAsync(KeyArtBlobs.OptionPath(world.Id, id), stream, "image/png", CancellationToken.None);
        }

        // Copied first: the write may update the very instance it was read from.
        List<Guid> options = [id, .. world.KeyArtOptionIds];
        await _keyArt.SaveKeyArtWorkAsync(world.Id, world.KeyArtBrief, options, CancellationToken.None);
        _logger.LogInformation("key_art_refined {WorldId} from={OptionId}", world.Id, command.OptionId);

        return AppResult<KeyArtState>.Success(await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>Makes one of the options on offer the key art. The art points at the option's blob; nothing is copied.</summary>
    public async Task<AppResult<KeyArtState>> ChooseAsync(ChooseKeyArtCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (!world!.KeyArtOptionIds.Contains(command.OptionId))
        {
            return Fail(OptionGone());
        }

        await ReplaceAsync(world, new WorldKeyArt
        {
            BlobPath = KeyArtBlobs.OptionPath(world.Id, command.OptionId),
            ContentType = "image/png",
            Source = CoverSource.Generated,
            Brief = world.KeyArtBrief,
            Anchor = world.KeyArt?.Anchor ?? KeyArtAnchor.Centre,
            UpdatedAt = _time.GetUtcNow(),
        }, ct);

        return AppResult<KeyArtState>.Success(await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>
    /// The GM's own picture: a PNG or JPEG read from its bytes, at most
    /// <see cref="KeyArtOptions.MaxUploadBytes"/>. Narrow art is taken with a warning, since the GM
    /// may know it's all they have.
    /// </summary>
    public async Task<AppResult<KeyArtState>> UploadAsync(UploadKeyArtCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (command.Bytes.Length == 0)
        {
            return Fail(new AppError(400, "invalid_image", "Choose a PNG or JPEG image."));
        }

        if (command.Bytes.Length > _options.MaxUploadBytes)
        {
            return Fail(new AppError(413, "image_too_large",
                $"Key art can be at most {_options.MaxUploadBytes / (1024 * 1024)} MB."));
        }

        if (ImageFile.Read(command.Bytes) is not { } info)
        {
            return Fail(new AppError(400, "invalid_image", "Key art must be a PNG or JPEG image."));
        }

        var path = KeyArtBlobs.UploadPath(world!.Id, Guid.NewGuid(), info.Extension);
        using (var stream = new MemoryStream(command.Bytes, writable: false))
        {
            await _blobs.UploadAsync(path, stream, info.ContentType, ct);
        }

        await ReplaceAsync(world, new WorldKeyArt
        {
            BlobPath = path,
            ContentType = info.ContentType,
            Source = CoverSource.Uploaded,
            Anchor = world.KeyArt?.Anchor ?? KeyArtAnchor.Centre,
            UpdatedAt = _time.GetUtcNow(),
        }, ct);

        var warning = info.Width < _options.SharpWidth
            ? $"This image is {info.Width} pixels wide, so it may look soft across the top of Home. Key art looks sharp at {_options.SharpWidth} pixels wide or more."
            : null;
        var state = await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct);
        return AppResult<KeyArtState>.Success(state with { Warning = warning });
    }

    public async Task<AppResult<KeyArtState>> SetAnchorAsync(SetKeyArtAnchorCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (world!.KeyArt is not { } art)
        {
            return Fail(new AppError(409, "no_key_art", "Add key art first, then choose where it sits."));
        }

        if (art.Anchor != command.Anchor)
        {
            await _keyArt.SetKeyArtAsync(world.Id, art with { Anchor = command.Anchor }, ct);
        }

        return AppResult<KeyArtState>.Success(await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>Takes the art away; the hero falls back to the map. An option stays on offer; anything else is deleted.</summary>
    public async Task<AppResult<KeyArtState>> RemoveAsync(RemoveKeyArtCommand command, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(command.WorldId, command.ActingUserRole, ct);
        if (error is not null)
        {
            return Fail(error);
        }

        if (world!.KeyArt is not null)
        {
            await ReplaceAsync(world, null, ct);
        }

        return AppResult<KeyArtState>.Success(await StateAsync(await ReloadAsync(world.Id, ct), command.ActingUserId, ct));
    }

    /// <summary>
    /// The key art's image, for any member of the world: key art is party-visible by definition.
    /// The caller has already established membership; the public site goes through <see cref="OpenForPublicAsync"/>.
    /// </summary>
    public async Task<AppResult<KeyArtImage>> OpenAsync(Guid worldId, CancellationToken ct)
    {
        var world = await _worlds.GetByIdAsync(worldId, ct);
        return await OpenArtAsync(world, ct);
    }

    /// <summary>
    /// The key art for the public site. The caller passes the world it resolved by slug with public
    /// access on, as every <c>/api/public</c> read does; a world without it is never served.
    /// </summary>
    public Task<AppResult<KeyArtImage>> OpenForPublicAsync(World world, CancellationToken ct) =>
        world.PublicAccessEnabled ? OpenArtAsync(world, ct) : Task.FromResult(AppResult<KeyArtImage>.Fail(NotFound()));

    /// <summary>One of the options on offer, for the GM's card.</summary>
    public async Task<AppResult<KeyArtImage>> OpenOptionAsync(Guid worldId, Guid optionId, WorldRole role, CancellationToken ct)
    {
        var (world, error) = await LoadForGmAsync(worldId, role, ct);
        if (error is not null)
        {
            return AppResult<KeyArtImage>.Fail(error);
        }

        return world!.KeyArtOptionIds.Contains(optionId)
            && await _blobs.ReadAllBytesOrNullAsync(KeyArtBlobs.OptionPath(worldId, optionId), ct) is { } bytes
            ? AppResult<KeyArtImage>.Success(new KeyArtImage(bytes, "image/png"))
            : AppResult<KeyArtImage>.Fail(NotFound());
    }

    private async Task<AppResult<KeyArtImage>> OpenArtAsync(World? world, CancellationToken ct) =>
        world?.KeyArt is { } art && await _blobs.ReadAllBytesOrNullAsync(art.BlobPath, ct) is { } bytes
            ? AppResult<KeyArtImage>.Success(new KeyArtImage(bytes, art.ContentType))
            : AppResult<KeyArtImage>.Fail(NotFound());

    /// <summary>Sets the art, then deletes the blob it replaced unless that is still an option on offer.</summary>
    private async Task ReplaceAsync(World world, WorldKeyArt? art, CancellationToken ct)
    {
        var previous = world.KeyArt;
        var onOffer = world.KeyArtOptionIds.Select(id => KeyArtBlobs.OptionPath(world.Id, id)).ToList();
        await _keyArt.SetKeyArtAsync(world.Id, art, ct);
        if (previous is not null
            && previous.BlobPath != art?.BlobPath
            && !onOffer.Contains(previous.BlobPath))
        {
            await _blobs.DeleteBlobAsync(previous.BlobPath, CancellationToken.None);
        }
    }

    /// <summary>Images generated this month: a count from an earlier month is spent, not owed.</summary>
    private static int ImagesIn(World world, string month) =>
        world.KeyArtImagesMonth == month ? world.KeyArtImagesGenerated : 0;

    private async Task<KeyArtState> StateAsync(World world, Guid actingUserId, CancellationToken ct)
    {
        var cap = _options.MaxImagesPerMonth;
        var used = ImagesIn(world, ThisMonth);
        var hasKey = await _generator.CanGenerateAsync(AiCallScope.ForWorld(world.Id, actingUserId), ct);
        var blocker = !hasKey ? NoKeyHint
            : used >= cap ? CapReachedText
            : null;
        var chosen = world.KeyArt is { Source: CoverSource.Generated } art
            ? world.KeyArtOptionIds.FirstOrDefault(id => KeyArtBlobs.OptionPath(world.Id, id) == art.BlobPath)
            : default;

        return new KeyArtState(
            world.KeyArt,
            world.KeyArtBrief,
            world.KeyArtOptionIds,
            chosen == Guid.Empty ? null : chosen,
            used,
            cap,
            blocker is null,
            blocker);
    }

    private string CapReachedText =>
        $"This world has generated its {_options.MaxImagesPerMonth} images for this month. You can still upload your own art.";

    private AppError CapReached() => new(409, "key_art_cap", CapReachedText);

    private async Task<(World? World, AppError? Error)> LoadForGmAsync(Guid worldId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return (null, new AppError(403, "insufficient_role", "Only the GM can change the world's key art."));
        }

        var world = await _worlds.GetByIdAsync(worldId, ct);
        return world is null ? (null, NotFound()) : (world, null);
    }

    private async Task<World> ReloadAsync(Guid worldId, CancellationToken ct) =>
        await _worlds.GetByIdAsync(worldId, ct)
        ?? throw new InvalidOperationException($"World '{worldId}' vanished during a key-art change.");

    private static AppError NotFound() => new(404, "not_found", "Key art not found.");

    private static AppError OptionGone() => new(404, "not_found", "That option is gone. Generate again or pick another.");

    private static AppResult<KeyArtState> Fail(AppError error) => AppResult<KeyArtState>.Fail(error);
}
