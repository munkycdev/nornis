using System.Net;
using Nornis.Application.Ai;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// Retry classification, which costs real money in both directions: a genuine throttle called
/// permanent writes off everything already spent on that source and needs a manual re-run, while
/// a permanent rejection called transient re-pays the whole pipeline once per delivery until the
/// queue gives up — five times, on the production queues.
///
/// The two implementations this replaced decided by substring-matching exception messages, and
/// had already drifted from each other on timeouts.
/// </summary>
[TestFixture]
public class TransientFailureClassifierTests
{
    private static HttpRequestException Http(HttpStatusCode status) =>
        new($"AI call failed: HTTP {(int)status}", inner: null, statusCode: status);

    // ------------------------------------------------------------------ transient

    [TestCase(HttpStatusCode.TooManyRequests)]     // 429 — the common one
    [TestCase(HttpStatusCode.RequestTimeout)]      // 408
    [TestCase(HttpStatusCode.InternalServerError)] // 500
    [TestCase(HttpStatusCode.BadGateway)]          // 502
    [TestCase(HttpStatusCode.ServiceUnavailable)]  // 503
    [TestCase(HttpStatusCode.GatewayTimeout)]      // 504
    public void ServiceSideStatuses_AreTransient(HttpStatusCode status)
    {
        Assert.That(TransientFailureClassifier.IsTransient(Http(status)), Is.True);
        Assert.That(TransientFailureClassifier.IsPermanentHttpFailure(Http(status)), Is.False);
    }

    [Test]
    public void TimeoutsAreTransient_OnBothPaths()
    {
        // Extraction used not to treat a timeout as transient while library indexing did — the
        // same outage retried one and wrote off the other.
        Assert.Multiple(() =>
        {
            Assert.That(TransientFailureClassifier.IsTransient(new TimeoutException()), Is.True);
            Assert.That(
                TransientFailureClassifier.IsTransient(new AiTimeoutException("timed out")),
                Is.True);
        });
    }

    [Test]
    public void NetworkFailureWithNoStatus_IsTransient()
    {
        // A socket or DNS failure never reached the service, so the request was never rejected.
        Assert.That(TransientFailureClassifier.IsTransient(new HttpRequestException("connection refused")), Is.True);
    }

    // ------------------------------------------------------------------ permanent

    [TestCase(HttpStatusCode.BadRequest)]    // 400 — the 2026-07-27 max_tokens outage
    [TestCase(HttpStatusCode.Unauthorized)]  // 401
    [TestCase(HttpStatusCode.Forbidden)]     // 403
    [TestCase(HttpStatusCode.NotFound)]      // 404
    public void RequestSideStatuses_ArePermanent(HttpStatusCode status)
    {
        Assert.That(TransientFailureClassifier.IsPermanentHttpFailure(Http(status)), Is.True);
        Assert.That(TransientFailureClassifier.IsTransient(Http(status)), Is.False);
    }

    [Test]
    public void TheOutageThatCausedThis_IsClassifiedPermanent()
    {
        // Regression pin: an unsupported parameter is a rejection of the request itself. Retrying
        // it re-sends the identical body and fails identically — five times, for nothing.
        var ex = new HttpRequestException(
            "AI call failed: HTTP 400 (invalid_request_error: unsupported_parameter) Parameter: max_tokens",
            inner: null,
            statusCode: HttpStatusCode.BadRequest);

        Assert.Multiple(() =>
        {
            Assert.That(TransientFailureClassifier.IsTransient(ex), Is.False);
            Assert.That(TransientFailureClassifier.IsPermanentHttpFailure(ex), Is.True);
        });
    }

    [Test]
    public void A429WhoseMessageMentionsNothing_IsStillTransient()
    {
        // The old substring matcher needed the literal "429" in the text. A status code does not
        // depend on how the SDK phrases itself.
        var ex = new HttpRequestException("throttled", inner: null, statusCode: HttpStatusCode.TooManyRequests);

        Assert.That(TransientFailureClassifier.IsTransient(ex), Is.True);
    }

    [Test]
    public void A400WhoseMessageQuotesAServiceCode_IsStillPermanent()
    {
        // The old matcher would have seen "503" in the quoted content and retried a rejection.
        var ex = new HttpRequestException(
            "AI call failed: HTTP 400 — the note contains the text 'error 503' which failed validation",
            inner: null,
            statusCode: HttpStatusCode.BadRequest);

        Assert.Multiple(() =>
        {
            Assert.That(TransientFailureClassifier.IsTransient(ex), Is.False);
            Assert.That(TransientFailureClassifier.IsPermanentHttpFailure(ex), Is.True);
        });
    }

    // ------------------------------------------------------------------ cancellation

    [Test]
    public void Cancellation_IsNeverTransient()
    {
        // Shutdown or an abandoned request is the caller's decision, not a service failure.
        Assert.That(TransientFailureClassifier.IsTransient(new OperationCanceledException()), Is.False);
        Assert.That(TransientFailureClassifier.IsTransient(new TaskCanceledException()), Is.False);
    }

    // ------------------------------------------------------------------ fallback

    [Test]
    public void UnknownExceptionWithNoStatus_FallsBackToPermanent()
    {
        // Erring toward permanent is deliberate: a wrong "transient" costs a full retry cycle of
        // paid work, a wrong "permanent" costs one visible failure the GM can re-run.
        Assert.That(TransientFailureClassifier.IsTransient(new InvalidOperationException("something odd")), Is.False);
    }

    [TestCase("Rate limit exceeded for this deployment")]
    [TestCase("Service unavailable, please retry")]
    [TestCase("Too many requests, slow down")]
    public void UnknownExceptionNamingAThrottle_IsTransient(string message)
    {
        // Narrow last resort for exceptions that expose no status at all.
        Assert.That(TransientFailureClassifier.IsTransient(new InvalidOperationException(message)), Is.True);
    }

    [Test]
    public void MessageMatchingIsAKnownWeakSpot_AndIsNotWorthChasing()
    {
        // "The service is temporarily unavailable" does not contain the contiguous substring
        // "service unavailable", so the fallback misses it — exactly the near-miss that made the
        // old message-matching implementations unreliable.
        //
        // Pinned rather than fixed on purpose. Widening the phrase list is an arms race against
        // wording nobody guarantees; the real mechanism is the status code, and every failure
        // that reaches here through the AI clients carries one. This test exists so the next
        // person recognises the limitation instead of rediscovering it as a bug.
        var nearMiss = new InvalidOperationException("The service is temporarily unavailable");

        Assert.That(TransientFailureClassifier.IsTransient(nearMiss), Is.False);

        // The same condition arriving with its status attached is classified correctly.
        Assert.That(TransientFailureClassifier.IsTransient(Http(HttpStatusCode.ServiceUnavailable)), Is.True);
    }
}
