using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace Nornis.Api.Caching;

/// <summary>
/// Removes the query string from the public cache key.
///
/// <para>The default output-cache policy varies by every query key. On endpoints that read query
/// parameters that is the only safe thing to do — but the public reads take all their input from
/// the route, and none of them looks at <c>Request.Query</c> at all. So <c>?_=1</c> and
/// <c>?_=2</c> produce byte-identical responses while occupying two cache entries.</para>
///
/// <para>That is not merely wasteful. Every distinct query string is a guaranteed miss, so walking
/// <c>?_=1..n</c> against a real slug is a way to fill a 100 MB in-memory store, compact genuine
/// entries out of it, and spend the anonymous rate-limit budget that other visitors share — while
/// never being served from cache once. Ignoring the query string collapses the whole family onto
/// one entry, which is also simply the correct key for a response that does not depend on it.
/// </para>
/// </summary>
public sealed class IgnoreQueryStringPolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        // Runs after the default policy, which sets this to "*".
        context.CacheVaryByRules.QueryKeys = StringValues.Empty;
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
