using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Application.Accounts;
using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// Profile › AI keys and Models, and the trial line (feature 39). Not world-scoped: every route
/// acts on the signed-in user's own keys, and none takes another user's id. The rules are
/// <see cref="AiKeyService"/>'s; this parses the route and maps the shapes.
/// </summary>
[ApiController]
[Route("api/me")]
public class AiKeysController : ControllerBase
{
    /// <summary>The limiter on saving a key: each save is a call to the provider with the key, and
    /// without a ceiling a stolen session could use Nornis to test keys.</summary>
    public const string KeyCheckRateLimit = "ai-key-check";

    private readonly IAiKeyService _keys;

    public AiKeysController(IAiKeyService keys)
    {
        _keys = keys;
    }

    [HttpGet("ai-keys")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok((await _keys.ListAsync(HttpContext.GetNornisUser().Id, ct)).Select(ToResponse));

    [HttpPut("ai-keys/{provider}")]
    [EnableRateLimiting(KeyCheckRateLimit)]
    public async Task<IActionResult> Save(string provider, [FromBody] SaveAiKeyRequest request, CancellationToken ct)
    {
        if (!TryParse<AiProvider>(provider, out var parsedProvider))
        {
            return Invalid("invalid_provider", "There is no such AI provider.");
        }

        var lines = new List<AiCompatibleModelLine>();
        foreach (var line in request.Models ?? [])
        {
            if (!TryParse<AiTask>(line.Task, out var task))
            {
                return Invalid("invalid_models", $"There is no kind of AI work called '{line.Task}'.");
            }

            lines.Add(new AiCompatibleModelLine(task, line.ModelName ?? string.Empty, line.SupportsImages,
                line.InputPerMillionUsd, line.OutputPerMillionUsd));
        }

        var result = await _keys.SaveAsync(new SaveAiKeyCommand(
            HttpContext.GetNornisUser().Id, parsedProvider, request.Key, request.Label, request.BaseUrl,
            request.Models is null ? null : lines), ct);

        return result.IsSuccess
            ? Ok(new SaveAiKeyResponse(ToResponse(result.Value!.Key), result.Value.SourcesQueued))
            : result.Error!.ToActionResult();
    }

    [HttpDelete("ai-keys/{provider}")]
    public async Task<IActionResult> Delete(string provider, CancellationToken ct)
    {
        if (!TryParse<AiProvider>(provider, out var parsedProvider))
        {
            return Invalid("invalid_provider", "There is no such AI provider.");
        }

        var result = await _keys.DeleteAsync(HttpContext.GetNornisUser().Id, parsedProvider, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    [HttpGet("ai-models")]
    public async Task<IActionResult> Models(CancellationToken ct) =>
        Ok((await _keys.ListModelsAsync(HttpContext.GetNornisUser().Id, ct)).Select(t => new AiTaskModelsResponse(
            t.Task.ToString(),
            t.Options.Select(o => new AiModelOptionResponse(o.Id, o.Name, o.Provider.ToString(), o.SupportsImages, o.HasKey, o.Tested)).ToList(),
            t.ChosenId,
            t.DefaultId)));

    [HttpPut("ai-models/{task}")]
    public async Task<IActionResult> Choose(string task, [FromBody] ChooseAiModelRequest request, CancellationToken ct)
    {
        if (!TryParse<AiTask>(task, out var parsedTask))
        {
            return Invalid("invalid_task", "There is no such kind of AI work.");
        }

        var result = await _keys.ChooseModelAsync(HttpContext.GetNornisUser().Id, parsedTask, request.ModelId, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    [HttpGet("ai-trial")]
    public async Task<IActionResult> Trial(CancellationToken ct)
    {
        var trial = await _keys.GetTrialAsync(HttpContext.GetNornisUser().Id, ct);
        return Ok(new AiTrialResponse(trial.InUse, trial.AllowanceUsd, trial.SpentUsd, trial.RemainingUsd, trial.Available));
    }

    internal static AiKeyResponse ToResponse(AiKeyView key) => new(
        key.Provider.ToString(), key.Label, key.LastFour, key.BaseUrl, key.AddedAt, key.UpdatedAt, key.CheckedAt,
        key.CheckFailed,
        key.Models.Select(m => new AiCompatibleModelResponse(
            m.Task.ToString(), m.ModelName, m.SupportsImages, m.InputPerMillionUsd, m.OutputPerMillionUsd)).ToList());

    /// <summary>Names only: a number would bind any value of the enum's underlying type.</summary>
    private static bool TryParse<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed)
        && Enum.IsDefined(parsed)
        && !int.TryParse(value, out _);

    private static IActionResult Invalid(string code, string message) =>
        new AppError(400, code, message).ToActionResult();
}
