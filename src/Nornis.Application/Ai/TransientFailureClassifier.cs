using System.Net;

namespace Nornis.Application.Ai;

/// <summary>
/// Decides whether an AI or storage failure is worth retrying.
///
/// One definition, because the copies this replaced had already started to drift — library
/// indexing handled <see cref="TimeoutException"/> and extraction's matcher did not, and a third
/// copy of the permanent-failure half was living in relationship backfill.
///
/// Both directions of a wrong answer cost real work. Classifying a genuine throttle as permanent
/// marks the source Failed and writes off everything already spent on it, needing a manual re-run
/// that pays again. Classifying a permanent rejection as transient feeds it back into the retry
/// loop until the queue's delivery count is exhausted, re-paying the whole pipeline each time.
///
/// The old implementations decided by substring-matching exception messages for "429", "503",
/// "rate limit" and "service unavailable". That reads the one part of an exception nobody
/// guarantees: SDK wording changes, messages get localised, and a wrapped exception can carry a
/// status code in text that means something else entirely. On 2026-07-27 an
/// <c>HTTP 400 unsupported_parameter</c> took every AI feature down — correctly classified as
/// permanent only because none of those four literals happened to appear in it.
/// </summary>
public static class TransientFailureClassifier
{
    /// <summary>
    /// True when retrying the identical request could plausibly succeed later.
    ///
    /// Decided on typed status codes wherever the exception carries one. The message is consulted
    /// only as a last resort, for exceptions that expose nothing else.
    /// </summary>
    /// <remarks>
    /// Note what is NOT referenced here: the Azure OpenAI SDK's own exception type. The
    /// infrastructure clients already translate it into <see cref="HttpRequestException"/>
    /// carrying the real status, which keeps the SDK out of the application layer — so this can
    /// decide on typed data without depending on the vendor.
    /// </remarks>
    public static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => IsTransientStatus((int)status),

        // A timeout says nothing about the request's validity — the service may simply have been
        // busy. (Extraction catches AiExtractionTimeoutException explicitly, before reaching
        // here, and already treated it as transient — so this arm changes nothing for that path.
        // It matters for library indexing, whose own matcher was the only one that handled
        // TimeoutException, and it keeps the two from disagreeing again.)
        TimeoutException or AiExtractionTimeoutException => true,

        // A socket or DNS failure with no status is an infrastructure problem, not a bad request.
        HttpRequestException => true,

        // Cancellation is the caller's decision, never a service failure.
        OperationCanceledException => false,

        _ => IsTransientByMessage(ex),
    };

    /// <summary>
    /// True when the request itself is the problem, so re-sending it byte-for-byte cannot help.
    /// 408 and 429 are excluded: both are 4xx but both mean "try again".
    /// </summary>
    public static bool IsPermanentHttpFailure(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => IsPermanentStatus((int)status),
        _ => false,
    };

    private static bool IsTransientStatus(int status) =>
        status == (int)HttpStatusCode.RequestTimeout        // 408
        || status == (int)HttpStatusCode.TooManyRequests    // 429
        || status >= 500;                                   // the service is having a bad time

    private static bool IsPermanentStatus(int status) =>
        status is >= 400 and < 500
        && status != (int)HttpStatusCode.RequestTimeout
        && status != (int)HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Last resort for exceptions carrying no status. Deliberately narrow: it errs toward
    /// "permanent", because a wrong "transient" costs a full retry cycle of paid work whereas a
    /// wrong "permanent" costs one visible failure the GM can re-run.
    /// </summary>
    private static bool IsTransientByMessage(Exception ex) =>
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);
}
