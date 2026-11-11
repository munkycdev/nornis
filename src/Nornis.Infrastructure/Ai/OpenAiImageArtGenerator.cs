using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Application.Art;
using Nornis.Domain.Enums;
using OpenAI.Images;

// The image options gpt-image models take (quality, file format) and the usage they report are
// marked experimental in the SDK. The art needs all three, and the price depends on the usage.
#pragma warning disable OPENAI001

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Pictures on the key holder's OpenAI image model, bound per call through
/// <see cref="IAiImageBinder"/>: the same funding rule, users' keys and failure translation as
/// every chat call. Covers (feature 38) are portrait, key art (feature 42) landscape; drafts are
/// PNG at medium quality, a page or a band rather than a poster, and a refine (feature 45) redraws
/// one at high quality through the edit endpoint.
/// </summary>
public sealed class OpenAiImageArtGenerator : IImageArtGenerator
{
    private readonly IAiImageBinder _binder;
    private readonly IAiFundingService _funding;
    private readonly ILogger<OpenAiImageArtGenerator> _logger;

    public OpenAiImageArtGenerator(IAiImageBinder binder, IAiFundingService funding, ILogger<OpenAiImageArtGenerator> logger)
    {
        _binder = binder;
        _funding = funding;
        _logger = logger;
    }

    /// <summary>
    /// The size the image models make for each shape: portrait is the nearest to a US Letter page,
    /// landscape the same frame turned on its side.
    /// </summary>
    public static (int Width, int Height) SizeOf(ImageShape shape) => shape switch
    {
        ImageShape.Portrait => (1024, 1536),
        ImageShape.Landscape => (1536, 1024),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Every image shape needs a size."),
    };

    public Task<ImageArtGeneration> GenerateAsync(
        AiCallScope scope, string prompt, ImageShape shape, int count, int timeoutSeconds, string operation, CancellationToken ct)
    {
        var (width, height) = SizeOf(shape);
        var options = new ImageGenerationOptions
        {
            Size = new GeneratedImageSize(width, height),
            Quality = GeneratedImageQuality.MediumQuality,
            OutputFileFormat = GeneratedImageFileFormat.Png,
        };
        return CallAsync(scope, AiTask.CoverArt, timeoutSeconds, operation,
            (client, token) => client.GenerateImagesAsync(prompt, count, options, token), ct);
    }

    /// <summary>
    /// The edit endpoint, at high quality: a refine is the finish, so it is worth the dearer image.
    /// <c>input_fidelity</c> and <c>complexity</c> are not sent; the 2.6 models' support for them
    /// is not documented, and an unsupported parameter fails the call.
    /// </summary>
    public Task<ImageArtGeneration> RefineAsync(
        AiCallScope scope, byte[] sourcePng, string prompt, ImageShape shape, int timeoutSeconds, string operation, CancellationToken ct)
    {
        var (width, height) = SizeOf(shape);
        var options = new ImageEditOptions
        {
            Size = new GeneratedImageSize(width, height),
            Quality = GeneratedImageQuality.HighQuality,
            OutputFileFormat = GeneratedImageFileFormat.Png,
        };
        return CallAsync(scope, AiTask.ArtRefinement, timeoutSeconds, operation, async (client, token) =>
        {
            using var image = new MemoryStream(sourcePng, writable: false);
            return await client.GenerateImageEditsAsync(image, "draft.png", prompt, 1, options, token);
        }, ct);
    }

    /// <summary>One bound image call: the binding, the timeout, the failure translation and the usage, shared by both kinds.</summary>
    private async Task<ImageArtGeneration> CallAsync(
        AiCallScope scope, AiTask task, int timeoutSeconds, string operation,
        Func<ImageClient, CancellationToken, Task<ClientResult<GeneratedImageCollection>>> call, CancellationToken ct)
    {
        var binding = await _binder.BindImagesAsync(scope, task, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        GeneratedImageCollection images;
        try
        {
            images = (await call(binding.Client, timeout.Token)).Value;
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
                _logger.LogWarning("{Operation} AI call timed out after {TimeoutSeconds}s", operation, timeoutSeconds);
                failure = new AiTimeoutException($"{operation} AI call timed out after {timeoutSeconds} seconds.");
            }
            else if (OpenAiProviderTraits.Instance.TranslateFailure(ex, binding.Funding, operation, _logger) is { } translated)
            {
                failure = translated is { StatusCode: 400 } && ex is ClientResultException rejected
                    ? Rejection(rejected, translated)
                    : translated;
            }
            else
            {
                _logger.LogError(ex, "Unexpected error during {Operation} AI call", operation);
                failure = new AiHttpException($"Unexpected error during {operation} AI call.", null, ex);
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
            .Select(i => new Nornis.Application.Art.GeneratedImage(i.ImageBytes.ToArray(), i.RevisedPrompt))
            .ToList();
        return new ImageArtGeneration(pictures, usage);
    }

    /// <summary>
    /// A 400 from the image endpoint, read from OpenAI's error body: its safety system's codes
    /// mean the prompt was turned down; anything else is passed on in OpenAI's own words.
    /// </summary>
    internal static ImageArtRejectedException Rejection(ClientResultException ex, Exception inner)
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
        return new ImageArtRejectedException(moderation, message, inner);
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
