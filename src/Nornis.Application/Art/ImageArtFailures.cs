using Nornis.Application.Ai;
using Nornis.Application.Errors;

namespace Nornis.Application.Art;

/// <summary>
/// What a picture's card says when a generation fails, in its own words: the error codes and the
/// sentences differ between a cover and key art, the reading of the failure doesn't.
/// </summary>
/// <param name="RefusedCode">The error code when the image model's safety check turned the brief down.</param>
/// <param name="Refused">What to tell the GM then: rewording can fix it.</param>
/// <param name="RejectedCode">The error code when the provider names another problem.</param>
/// <param name="RejectedTail">The sentence after the provider's own reason, saying nothing was spent.</param>
/// <param name="Unavailable">What to say when the provider failed or timed out.</param>
public sealed record ImageArtWords(string RefusedCode, string Refused, string RejectedCode, string RejectedTail, string Unavailable);

/// <summary>
/// How a failed image call reads to the GM, decided once for covers (feature 38) and key art
/// (feature 42). A moderation block is the brief's fault and rewording can fix it; any other 400 is
/// the provider naming a problem (billing, the account, the model), passed on in its own words; a
/// refusal is the funding rule's own message.
/// </summary>
public static class ImageArtFailures
{
    private const int MaxReasonChars = 300;

    /// <returns>The ledger's error code and the error to answer with.</returns>
    public static (string Code, AppError Error) Classify(Exception ex, ImageArtWords words) => ex switch
    {
        AiRefusalException refusal => (refusal.Category, refusal.ToAppError()),
        ImageArtRejectedException { Moderation: true } =>
            ("ContentRefused", new AppError(422, words.RefusedCode, words.Refused)),
        ImageArtRejectedException rejected =>
            ("Rejected", new AppError(422, words.RejectedCode,
                $"OpenAI didn't accept the request: {RejectionReason(rejected.Reason)} {words.RejectedTail}")),
        AiHttpException { StatusCode: 429 } =>
            ("RateLimited", new AppError(429, "rate_limited", "Too many requests. Please try again in a moment.")),
        AiTimeoutException or OperationCanceledException => ("Timeout", new AppError(503, "service_unavailable", words.Unavailable)),
        _ => ("ServiceError", new AppError(503, "service_unavailable", words.Unavailable)),
    };

    private static string RejectionReason(string? reason)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? "it gave no reason." : reason.Trim();
        text = text.Length > MaxReasonChars ? text[..MaxReasonChars].TrimEnd() + "…" : text;
        return text.EndsWith('.') || text.EndsWith('…') ? text : text + ".";
    }
}
