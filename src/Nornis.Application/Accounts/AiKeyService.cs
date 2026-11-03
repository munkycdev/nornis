using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Accounts;

/// <summary>One of the user's keys as Profile shows it. The key itself never leaves the API.</summary>
public sealed record AiKeyView(
    AiProvider Provider,
    string Label,
    string LastFour,
    string? BaseUrl,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CheckedAt,
    bool CheckFailed,
    IReadOnlyList<AiCompatibleModelLine> Models);

/// <summary>One task's model on an OpenAI-compatible key, as the user enters it.</summary>
public sealed record AiCompatibleModelLine(
    AiTask Task, string ModelName, bool SupportsImages, decimal InputPerMillionUsd, decimal OutputPerMillionUsd);

/// <param name="Key">Null or blank keeps the stored key and checks it again.</param>
/// <param name="BaseUrl">For <see cref="AiProvider.OpenAiCompatible"/> only.</param>
/// <param name="Models">For <see cref="AiProvider.OpenAiCompatible"/> only: one line per task it serves.</param>
public sealed record SaveAiKeyCommand(
    Guid UserId,
    AiProvider Provider,
    string? Key,
    string? Label,
    string? BaseUrl,
    IReadOnlyList<AiCompatibleModelLine>? Models);

/// <param name="SourcesQueued">Sources that had failed for want of a key and were queued again.</param>
public sealed record SavedAiKey(AiKeyView Key, int SourcesQueued);

/// <param name="Id">What a choice stores: a catalog id, <c>compatible:{line id}</c>, or
/// <c>untested:{provider}/{model}</c>.</param>
/// <param name="HasKey">Whether the user has a key for its provider; a model without one can't be picked.</param>
/// <param name="Tested">False for OpenAI-compatible models and for the models a key's provider lists
/// beyond the catalog ("More from your key"), which Nornis has never run.</param>
public sealed record AiModelOption(string Id, string Name, AiProvider Provider, bool SupportsImages, bool HasKey, bool Tested);

/// <param name="ChosenId">The user's pick, or null for the default.</param>
/// <param name="DefaultId">What runs the task with no pick; null when nothing of theirs can.</param>
public sealed record AiTaskModels(AiTask Task, IReadOnlyList<AiModelOption> Options, string? ChosenId, string? DefaultId);

/// <param name="InUse">Whether Nornis pays through trials at all (<see cref="AiFallback.Trial"/>).</param>
/// <param name="Available">Whether the next call could spend it: in use, a balance left, and room
/// under this month's ceiling for all trials.</param>
public sealed record AiTrialView(bool InUse, decimal AllowanceUsd, decimal SpentUsd, decimal RemainingUsd, bool Available);

/// <summary>A world's AI funding as its GM sees it.</summary>
/// <param name="Trial">At least one kind of AI work in the world would spend its creator's trial.</param>
/// <param name="TrialRemainingUsd">The creator's trial balance, when <paramref name="Trial"/>.</param>
/// <param name="CreatorIsYou">Whether the GM asking is the creator, whose keys the world spends.</param>
public sealed record WorldAiFundingView(bool Trial, decimal? TrialRemainingUsd, bool CreatorIsYou);

public interface IAiKeyService
{
    Task<IReadOnlyList<AiKeyView>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>Checks the key with its provider, then adds it or replaces the user's key for that
    /// provider; then queues again the sources that failed for want of a key.</summary>
    Task<AppResult<SavedAiKey>> SaveAsync(SaveAiKeyCommand command, CancellationToken ct);

    Task<AppResult> DeleteAsync(Guid userId, AiProvider provider, CancellationToken ct);

    Task<IReadOnlyList<AiTaskModels>> ListModelsAsync(Guid userId, CancellationToken ct);

    /// <summary>Sets the user's model for a task; null or blank goes back to the default.</summary>
    Task<AppResult> ChooseModelAsync(Guid userId, AiTask task, string? modelId, CancellationToken ct);

    Task<AiTrialView> GetTrialAsync(Guid userId, CancellationToken ct);

    Task<AppResult<WorldAiFundingView>> GetWorldFundingAsync(Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}

/// <summary>
/// Profile's AI keys and Models cards, and the trial line (feature 39). Every call acts on the
/// signed-in user's own keys: nothing here takes another user's id from a request.
/// </summary>
public sealed class AiKeyService : IAiKeyService
{
    /// <summary>Sources queued again per query when a key is saved.</summary>
    public const int RequeueBatchSize = 50;

    public const int MaxKeyLength = 1000;
    public const int MaxLabelLength = 100;
    public const int MaxBaseUrlLength = 500;
    public const int MaxModelNameLength = 200;
    public const decimal MaxPricePerMillionUsd = 1000m;

    private readonly IAiKeyRepository _keys;
    private readonly IAiKeyProtector _protector;
    private readonly IAiKeyChecker _checker;
    private readonly IAiModelLister _lister;
    private readonly IAiUsageRecordRepository _usage;
    private readonly IWorldRepository _worlds;
    private readonly ISourceRepository _sources;
    private readonly IWorldMemberRepository _members;
    private readonly ISourceService _sourceService;
    private readonly AiModelsOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AiKeyService> _logger;

    public AiKeyService(
        IAiKeyRepository keys,
        IAiKeyProtector protector,
        IAiKeyChecker checker,
        IAiModelLister lister,
        IAiUsageRecordRepository usage,
        IWorldRepository worlds,
        ISourceRepository sources,
        IWorldMemberRepository members,
        ISourceService sourceService,
        IOptions<AiModelsOptions> options,
        TimeProvider time,
        ILogger<AiKeyService> logger)
    {
        _keys = keys;
        _protector = protector;
        _checker = checker;
        _lister = lister;
        _usage = usage;
        _worlds = worlds;
        _sources = sources;
        _members = members;
        _sourceService = sourceService;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiKeyView>> ListAsync(Guid userId, CancellationToken ct) =>
        (await _keys.ListByUserAsync(userId, ct)).Select(ToView).ToList();

    public async Task<AppResult<SavedAiKey>> SaveAsync(SaveAiKeyCommand command, CancellationToken ct)
    {
        if (!Callable(command.Provider))
        {
            return Fail(400, "provider_not_supported",
                $"Nornis can't use {AiProviderNames.Display(command.Provider)} keys yet.");
        }

        var existing = (await _keys.ListByUserAsync(command.UserId, ct)).FirstOrDefault(k => k.Provider == command.Provider);

        var secret = command.Key?.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            // Saving without a key keeps the stored one, checked again: how a user clears a
            // failed check, or edits a compatible key's models, without pasting the key anew.
            secret = existing is null ? null : _protector.Unprotect(existing.ProtectedKey);
            if (secret is null)
            {
                return Fail(400, "key_required", existing is null
                    ? "Paste the key to add it."
                    : "Nornis can no longer read the key it had. Paste it again.");
            }
        }
        else if (secret.Length is < 8 or > MaxKeyLength || secret.Any(char.IsWhiteSpace))
        {
            return Fail(400, "invalid_key", "That doesn't look like a key. Paste it again, without spaces.");
        }

        var label = string.IsNullOrWhiteSpace(command.Label) ? AiProviderNames.Display(command.Provider) : command.Label.Trim();
        if (label.Length > MaxLabelLength)
        {
            return Fail(400, "invalid_label", $"Keep the name to {MaxLabelLength} characters.");
        }

        var compatible = command.Provider == AiProvider.OpenAiCompatible;
        Uri? baseUrl = null;
        if (compatible)
        {
            if (BaseUrlProblem(command.BaseUrl, out baseUrl) is { } problem)
            {
                return Fail(400, "invalid_base_url", problem);
            }
        }
        else if (!string.IsNullOrWhiteSpace(command.BaseUrl))
        {
            return Fail(400, "invalid_base_url", "Only an OpenAI-compatible key takes a base URL.");
        }

        var lines = command.Models ?? [];
        var modelsProblem = compatible ? ModelsProblem(lines)
            : lines.Count > 0 ? "Only an OpenAI-compatible key lists its own models."
            : null;
        if (modelsProblem is not null)
        {
            return Fail(400, "invalid_models", modelsProblem);
        }

        var outcome = await _checker.CheckAsync(command.Provider, secret, baseUrl, ct);
        if (outcome != AiKeyCheckOutcome.Accepted)
        {
            _logger.LogInformation("AI key check refused a save. UserId={UserId} Provider={Provider} Outcome={Outcome}",
                command.UserId, command.Provider, outcome);
            return AppResult<SavedAiKey>.Fail(CheckError(command.Provider, outcome));
        }

        string protectedKey;
        try
        {
            protectedKey = _protector.Protect(secret);
        }
        catch (AiKeyStorageException ex)
        {
            _logger.LogError(ex, "AI key couldn't be encrypted; nothing was saved. UserId={UserId} Provider={Provider}",
                command.UserId, command.Provider);
            return Fail(503, "ai_key_storage_unavailable",
                "Nornis couldn't store the key securely just now. Nothing was saved; try again in a few minutes.");
        }

        var now = _time.GetUtcNow();
        var key = new UserAiKey
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            UserId = command.UserId,
            Provider = command.Provider,
            Label = label,
            ProtectedKey = protectedKey,
            LastFour = secret[^4..],
            BaseUrl = baseUrl?.AbsoluteUri,
            AddedAt = existing?.AddedAt ?? now,
            UpdatedAt = now,
            CheckedAt = now,
            CheckFailed = false,
            CompatibleModels = lines.Select(l => new UserAiCompatibleModel
            {
                Id = Guid.NewGuid(),
                Task = l.Task,
                ModelName = l.ModelName.Trim(),
                SupportsImages = l.SupportsImages,
                InputPerMillionUsd = l.InputPerMillionUsd,
                OutputPerMillionUsd = l.OutputPerMillionUsd,
            }).ToList(),
        };

        await _keys.SaveAsync(key, ct);
        _logger.LogInformation("AI key saved. UserId={UserId} Provider={Provider} Replaced={Replaced}",
            command.UserId, command.Provider, existing is not null);

        var queued = await RequeueKeyFailedSourcesAsync(command.UserId, ct);
        return AppResult<SavedAiKey>.Success(new SavedAiKey(ToView(key), queued));
    }

    public async Task<AppResult> DeleteAsync(Guid userId, AiProvider provider, CancellationToken ct)
    {
        var existing = (await _keys.ListByUserAsync(userId, ct)).FirstOrDefault(k => k.Provider == provider);
        if (existing is null)
        {
            return AppResult.Fail(new AppError(404, "not_found", "There is no key for that provider."));
        }

        await _keys.DeleteAsync(existing.Id, ct);
        _logger.LogInformation("AI key removed. UserId={UserId} Provider={Provider}", userId, provider);
        return AppResult.Success();
    }

    public async Task<IReadOnlyList<AiTaskModels>> ListModelsAsync(Guid userId, CancellationToken ct)
    {
        var keys = await _keys.ListByUserAsync(userId, ct);
        var choices = await _keys.ListChoicesAsync(userId, ct);
        var lines = keys.FirstOrDefault(k => k.Provider == AiProvider.OpenAiCompatible)?.CompatibleModels ?? [];
        var untested = await UntestedModelsAsync(keys, ct);

        return Enum.GetValues<AiTask>().Select(task =>
        {
            var chosenId = choices.FirstOrDefault(c => c.Task == task)?.ModelId;
            var options = _options.Catalog
                .Where(m => m.Tasks.Contains(task))
                .Select(m => new AiModelOption(m.Id, m.Model, m.Provider, m.SupportsImages,
                    keys.Any(k => k.Provider == m.Provider), Tested: true))
                .Concat(lines.Where(l => l.Task == task).Select(l => new AiModelOption(
                    AiModelPicker.CompatibleId(l.Id), l.ModelName, AiProvider.OpenAiCompatible, l.SupportsImages,
                    HasKey: true, Tested: false)))
                .Concat(AiTasks.OnlyKeysRun(task) ? [] : untested)
                .ToList();

            // A pick the provider no longer lists, or couldn't be asked about just now, still
            // shows as the pick: it runs until it fails or the GM changes it.
            if (AiModelPicker.TryParseUntested(chosenId ?? "", out var provider, out var model)
                && options.All(o => o.Id != chosenId)
                && AiModelPicker.Pick(_options, keys, chosenId, task, needsImages: false)?.ChoiceId == chosenId)
            {
                options.Add(new AiModelOption(chosenId!, model, provider, SupportsImages: false, HasKey: true, Tested: false));
            }

            return new AiTaskModels(task, options, chosenId,
                AiModelPicker.Pick(_options, keys, null, task, needsImages: false)?.ChoiceId);
        }).ToList();
    }

    /// <summary>
    /// "More from your key": the chat models each of the user's keys can run that the catalog
    /// doesn't list, for any task but the key-only ones. A provider that can't be asked just now
    /// adds nothing; nor does a key whose check failed, or a provider the catalog can't price.
    /// </summary>
    private async Task<IReadOnlyList<AiModelOption>> UntestedModelsAsync(IReadOnlyList<UserAiKey> keys, CancellationToken ct)
    {
        var options = new List<AiModelOption>();
        foreach (var key in keys.Where(k => AiModelPicker.UntestedProviders.Contains(k.Provider) && !k.CheckFailed))
        {
            if (_options.EstimateFor(key.Provider) is null || _protector.Unprotect(key.ProtectedKey) is not { } secret)
            {
                continue;
            }

            var catalogued = _options.Catalog.Where(m => m.Provider == key.Provider).Select(m => m.Model).ToHashSet(StringComparer.Ordinal);
            var listed = await _lister.ListChatModelsAsync(key, secret, ct) ?? [];
            options.AddRange(listed
                .Where(m => !catalogued.Contains(m.Id))
                .Select(m => new AiModelOption(AiModelPicker.UntestedId(key.Provider, m.Id), m.Name, key.Provider,
                    SupportsImages: false, HasKey: true, Tested: false)));
        }

        return options;
    }

    public async Task<AppResult> ChooseModelAsync(Guid userId, AiTask task, string? modelId, CancellationToken ct)
    {
        if (!Enum.IsDefined(task))
        {
            return AppResult.Fail(new AppError(400, "invalid_task", "There is no such kind of AI work."));
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            await _keys.SetChoiceAsync(userId, task, null, ct);
            return AppResult.Success();
        }

        var offered = (await ListModelsAsync(userId, ct)).Single(t => t.Task == task).Options
            .FirstOrDefault(o => string.Equals(o.Id, modelId, StringComparison.Ordinal));
        if (offered is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_model", "That model can't do this kind of work."));
        }

        if (!offered.HasKey)
        {
            return AppResult.Fail(new AppError(400, "no_key_for_model",
                $"Add a {AiProviderNames.Display(offered.Provider)} key first."));
        }

        await _keys.SetChoiceAsync(userId, task, offered.Id, ct);
        return AppResult.Success();
    }

    public async Task<AiTrialView> GetTrialAsync(Guid userId, CancellationToken ct)
    {
        var spent = await _usage.SumTrialSpendAsync(userId, ct);
        var remaining = Math.Max(0m, _options.TrialAllowanceUsd - spent);
        var inUse = _options.Fallback == AiFallback.Trial;
        var available = inUse && remaining > 0 && await CeilingHasRoomAsync(ct);
        return new AiTrialView(inUse, _options.TrialAllowanceUsd, spent, remaining, available);
    }

    public async Task<AppResult<WorldAiFundingView>> GetWorldFundingAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        // Money is the GM's business; players are told nothing about it.
        if (role != WorldRole.GM)
        {
            return AppResult<WorldAiFundingView>.Fail(new AppError(403, "insufficient_role", "Only a GM can see how the world's AI is paid for."));
        }

        var world = await _worlds.GetByIdAsync(worldId, ct);
        if (world is null)
        {
            return AppResult<WorldAiFundingView>.Fail(new AppError(404, "not_found", "World not found."));
        }

        var creatorIsYou = world.CreatedByUserId == actingUserId;
        if (_options.Fallback != AiFallback.Trial)
        {
            return AppResult<WorldAiFundingView>.Success(new WorldAiFundingView(false, null, creatorIsYou));
        }

        var keys = await _keys.ListByUserAsync(world.CreatedByUserId, ct);
        var choices = await _keys.ListChoicesAsync(world.CreatedByUserId, ct);
        // Cover art never runs on the trial (feature 38), so a missing image key isn't "on the trial".
        var onTrial = Enum.GetValues<AiTask>().Where(task => !AiTasks.OnlyKeysRun(task)).Any(task =>
            AiModelPicker.Pick(_options, keys, choices.FirstOrDefault(c => c.Task == task)?.ModelId, task, needsImages: false) is null);

        decimal? remaining = onTrial
            ? Math.Max(0m, _options.TrialAllowanceUsd - await _usage.SumTrialSpendAsync(world.CreatedByUserId, ct))
            : null;
        return AppResult<WorldAiFundingView>.Success(new WorldAiFundingView(onTrial, remaining, creatorIsYou));
    }

    /// <summary>
    /// Queues again the sources in the user's worlds that failed for want of a key (feature 39,
    /// B6b), through <see cref="ISourceService.MarkReadyAsync"/>: the path the GM's own retry of a
    /// failed source takes, status ordering and enqueue-failure revert included. Only when the
    /// user's keys now cover extraction, or the sources would only fail again.
    /// </summary>
    private async Task<int> RequeueKeyFailedSourcesAsync(Guid userId, CancellationToken ct)
    {
        var keys = await _keys.ListByUserAsync(userId, ct);
        var choice = await _keys.GetChoiceAsync(userId, AiTask.Extraction, ct);
        if (AiModelPicker.Pick(_options, keys, choice?.ModelId, AiTask.Extraction, needsImages: false) is null)
        {
            return 0;
        }

        var attempted = new HashSet<Guid>();
        var roles = new Dictionary<Guid, WorldRole?>();
        var queued = 0;
        while (true)
        {
            var batch = await _sources.ListFailedInCreatedWorldsAsync(userId, ErrorCategories.Refusals, RequeueBatchSize, attempted, ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var source in batch)
            {
                attempted.Add(source.Id);
                if (!roles.TryGetValue(source.WorldId, out var role))
                {
                    role = (await _members.GetByWorldAndUserAsync(source.WorldId, userId, ct))?.Role;
                    roles[source.WorldId] = role;
                }

                if (role is not { } memberRole)
                {
                    continue; // the creator has left the world; its GMs retry by hand
                }

                var ready = await _sourceService.MarkReadyAsync(
                    new MarkSourceReadyCommand(source.Id, source.WorldId, userId, memberRole), ct);
                if (ready.IsSuccess)
                {
                    queued++;
                }
                else
                {
                    _logger.LogWarning("A key-failed source was not queued again. SourceId={SourceId} Code={Code}",
                        source.Id, ready.Error!.Code);
                }
            }
        }

        if (queued > 0)
        {
            _logger.LogInformation("Queued {Count} key-failed source(s) again after a key save. UserId={UserId}", queued, userId);
        }

        return queued;
    }

    private async Task<bool> CeilingHasRoomAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return await _usage.SumAllTrialSpendAsync(monthStart, ct) < _options.TrialMonthlyCeilingUsd;
    }

    /// <summary>A provider Nornis can call with a user's key: one with catalog models, or an
    /// OpenAI-compatible endpoint, which brings its own.</summary>
    public static bool Callable(AiProvider provider) =>
        provider == AiProvider.OpenAiCompatible || AiModelsOptions.CatalogProviders.Contains(provider);

    /// <summary>
    /// The shape of a compatible endpoint: absolute https, no credentials, query or fragment. Where
    /// it points is checked again on every connection (Infrastructure's public-endpoint guard),
    /// since a name can resolve somewhere else tomorrow.
    /// </summary>
    internal static string? BaseUrlProblem(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "An OpenAI-compatible key needs its endpoint's base URL.";
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxBaseUrlLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return "The base URL must be an https address with no user name, query or fragment, such as https://example.com/v1/.";
        }

        // The SDK appends paths to the base, so it has to end in a slash or its last segment is lost.
        uri = parsed.AbsolutePath.EndsWith('/') ? parsed : new Uri(parsed.AbsoluteUri + "/");
        return null;
    }

    internal static string? ModelsProblem(IReadOnlyList<AiCompatibleModelLine> lines)
    {
        if (lines.Count == 0)
        {
            return "Name the model this endpoint should use for at least one kind of AI work.";
        }

        if (lines.Select(l => l.Task).Distinct().Count() != lines.Count)
        {
            return "Name one model per kind of AI work.";
        }

        foreach (var line in lines)
        {
            if (!Enum.IsDefined(line.Task))
            {
                return "There is no such kind of AI work.";
            }

            if (AiTasks.OnlyKeysRun(line.Task))
            {
                return "An OpenAI-compatible endpoint can't make cover art.";
            }

            if (string.IsNullOrWhiteSpace(line.ModelName) || line.ModelName.Trim().Length > MaxModelNameLength)
            {
                return $"Each model needs a name of at most {MaxModelNameLength} characters.";
            }

            if (line.InputPerMillionUsd is < 0 or > MaxPricePerMillionUsd || line.OutputPerMillionUsd is < 0 or > MaxPricePerMillionUsd)
            {
                return $"Prices are USD per million tokens, from 0 (free) to {MaxPricePerMillionUsd}.";
            }
        }

        return null;
    }

    private static AppError CheckError(AiProvider provider, AiKeyCheckOutcome outcome)
    {
        var name = AiProviderNames.Display(provider);
        return outcome switch
        {
            AiKeyCheckOutcome.Rejected => new AppError(400, "ai_key_check_failed", $"{name} didn't accept that key. Nothing was saved."),
            AiKeyCheckOutcome.NotFound => new AppError(400, "ai_endpoint_not_found",
                "The endpoint answered, but has no model list at that base URL. Check the URL; nothing was saved."),
            _ => new AppError(503, "ai_key_check_unreachable", $"Nornis couldn't reach {name} to check the key. Nothing was saved; try again."),
        };
    }

    private static AiKeyView ToView(UserAiKey key) => new(
        key.Provider, key.Label, key.LastFour, key.BaseUrl, key.AddedAt, key.UpdatedAt, key.CheckedAt, key.CheckFailed,
        key.CompatibleModels.OrderBy(m => m.Task)
            .Select(m => new AiCompatibleModelLine(m.Task, m.ModelName, m.SupportsImages, m.InputPerMillionUsd, m.OutputPerMillionUsd))
            .ToList());

    private static AppResult<SavedAiKey> Fail(int status, string code, string message) =>
        AppResult<SavedAiKey>.Fail(new AppError(status, code, message));
}
