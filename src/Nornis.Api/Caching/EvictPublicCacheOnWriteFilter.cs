using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;

namespace Nornis.Api.Caching;

/// <summary>
/// Drops the anonymous public output cache after any successful write.
///
/// <para><b>Why a blanket rule instead of evicting where it matters.</b> The first version of this
/// evicted in the two places that obviously changed a public page — updating and deleting a world.
/// That missed the ones that matter most: setting a published session note to GMOnly, deleting a
/// source, removing a fact. Those are takedowns. A GM who spots a player's real name in a
/// published note and hides it would have gone on serving it to strangers for the rest of the
/// cache window.</para>
///
/// <para>And they would not have seen it. The Blazor host attaches the bearer token to every API
/// call, and output caching declines to serve a request carrying an <c>Authorization</c> header —
/// so the GM checking their own public page gets the live answer while anonymous visitors get the
/// cached one. The person best placed to notice is the one person who cannot.</para>
///
/// <para>Enumerating the write paths would have to be redone correctly every time a new one is
/// added, and getting it wrong is silent. Evicting on every successful mutation cannot be
/// forgotten. It costs a cold public read after each write, on a surface whose writes are rare and
/// whose reads are anonymous strangers.</para>
/// </summary>
public sealed class EvictPublicCacheOnWriteFilter : IAsyncResultFilter
{
    private readonly IOutputCacheStore _store;

    public EvictPublicCacheOnWriteFilter(IOutputCacheStore store)
    {
        _store = store;
    }

    /// <summary>Methods that cannot change what a public page shows.</summary>
    private static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var executed = await next();

        if (IsRead(context.HttpContext.Request.Method))
        {
            return;
        }

        // Only successful writes. A rejected one changed nothing, and evicting on every 403 would
        // hand any authenticated caller a way to keep the public cache permanently cold.
        var status = executed.HttpContext.Response.StatusCode;
        if (status is < 200 or >= 300)
        {
            return;
        }

        await PublicOutputCache.EvictAsync(_store, context.HttpContext.RequestAborted);
    }
}
