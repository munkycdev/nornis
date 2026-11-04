using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Application.Prep.Covers;
using Nornis.Domain.Enums;
using OpenAI.Images;

// The image options gpt-image models take (quality, file format) and the usage they report are
// marked experimental in the SDK. Cover art needs all three, and the price depends on the usage.
#pragma warning disable OPENAI001

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Cover art on the key holder's OpenAI image model (feature 38), bound per call through
/// <see cref="IAiImageBinder"/>: the same funding rule, users' keys and failure translation as
/// every chat call. Portrait, PNG, medium quality: a booklet page, not a poster.
/// </summary>
public sealed class OpenAiCoverArtGenerator : ICoverArtGenerator
{
    private const string Operation = "CoverArt";

    /// <summary>The portrait size closest to a US Letter page that the image models make.</summary>
    public const int Width = 1024;
    public const int Height = 1536;

    private readonly IAiImageBinder _binder;
    private readonly IAiFundingService _funding;
    private readonly ILogger<OpenAiCoverArtGenerator> _logger;

    public OpenAiCoverArtGenerator(IAiImageBinder binder, IAiFundingService funding, ILogger<OpenAiCoverArtGenerator> logger)
    {
        _binder = binder;
        _funding = funding;
        _logger = logger;
    }

    public async Task<CoverArtGeneration> GenerateAsync(AiCallScope scope, string prompt, int count, int timeoutSeconds, CancellationToken ct)
    {
        var binding = await _binder.BindImagesAsync(scope, AiTask.CoverArt, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        GeneratedImageCollection images;
        try
        {
            var options = new ImageGenerationOptions
            {
                Size = new GeneratedImageSize(Width, Height),
                Quality = GeneratedImageQuality.MediumQuality,
                OutputFileFormat = GeneratedImageFileFormat.Png,
            };
            images = (await binding.Client.GenerateImagesAsync(prompt, count, options, timeout.Token)).Value;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Exception failure;
            if (ex is OperationCanceledException)
            {
                _logger.LogWarning("{Operation} AI call timed out after {TimeoutSeconds}s", Operation, timeoutSeconds);
                failure = new AiTimeoutException($"{Operation} AI call timed out after {timeoutSeconds} seconds.");
            }
            else if (OpenAiProviderTraits.Instance.TranslateFailure(ex, binding.Funding, Operation, _logger) is { } translated)
            {
                failure = translated is { StatusCode: 400 } && ex is ClientResultException rejected
                    ? Rejection(rejected, translated)
                    : translated;
            }
            else
            {
                _logger.LogError(ex, "Unexpected error during {Operation} AI call", Operation);
                failure = new AiHttpException($"Unexpected error during {Operation} AI call.", null, ex);
            }

            await _binder.ReportAsync(binding, failure, CancellationToken.None);
            throw failure;
        }

        await _binder.ReportAsync(binding, null, CancellationToken.None);
        stopwatch.Stop();

        var usage = new AiUsage
        {
            Model = binding.Funding.Model,
            Funding = binding.Funding,
            InputTokens = (int)(images.Usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(images.Usage?.OutputTokenCount ?? 0),
            TotalTokens = (int)(images.Usage?.TotalTokenCount ?? 0),
            DurationMs = (int)stopwatch.ElapsedMilliseconds,
        };
        var pictures = images
            .Where(i => i.ImageBytes is not null)
            .Select(i => new GeneratedCoverImage(i.ImageBytes.ToArray(), i.RevisedPrompt))
            .ToList();
        return new CoverArtGeneration(pictures, usage);
    }

    /// <summary>
    /// A 400 from the image endpoint, read from OpenAI's error body: its safety system's codes
    /// mean the prompt was turned down; anything else is passed on in OpenAI's own words.
    /// </summary>
    internal static CoverArtRejectedException Rejection(ClientResultException ex, Exception inner)
    {
        string? code = null;
        string? message = null;
        try
        {
            var body = ex.GetRawResponse()?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                }
            }
        }
        catch (JsonException)
        {
            // Not OpenAI's error shape: no reason to pass on.
        }

        var moderation = code is "moderation_blocked" or "content_policy_violation"
            || (message?.Contains("safety system", StringComparison.OrdinalIgnoreCase) ?? false);
        return new CoverArtRejectedException(moderation, message, inner);
    }

    public async Task<bool> CanGenerateAsync(AiCallScope scope, CancellationToken ct)
    {
        try
        {
            await _funding.ResolveAsync(scope, AiTask.CoverArt, nornisModel: null, needsImages: false, ct);
            return true;
        }
        catch (AiRefusalException)
        {
            return false;
        }
    }
}
