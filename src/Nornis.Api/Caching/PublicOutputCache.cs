using Microsoft.AspNetCore.OutputCaching;

namespace Nornis.Api.Caching;

/// <summary>
/// Output caching for the anonymous public world pages.
///
/// <para><b>Why only here.</b> Every read under <c>/api/public/worlds/{slug}</c> runs as
/// <c>Observer</c> with a sentinel user id, so the response is a function of the URL alone. No
/// other surface in this API is: authenticated responses vary by user, by world role, and by the
/// view-as header, and a cache that missed any of those would hand one reader another's view of a
/// world. That is why this is a named policy applied per action rather than a base policy.</para>
///
/// <para><b>What it is worth.</b> The public pages are the one surface a stranger can reach — a
/// shared link, a crawler, a chat unfurl — and the one whose data changes least often. Caching
/// them means a burst of traffic on a link costs one set of queries rather than one per visitor.
/// </para>
/// </summary>
public static class PublicOutputCache
{
    public const string PolicyName = "public-read";

    /// <summary>
    /// Tag every public response carries, so a settings change can drop the lot.
    ///
    /// <para>Deliberately one tag for everything rather than one per slug. Eviction happens when a
    /// GM changes a world's public settings, and a slug change has to invalidate entries under
    /// <i>both</i> the old and new slug — the old one is the dangerous half, because it would
    /// otherwise keep serving a world that link no longer addresses. Tracking that correctly per
    /// slug is more moving parts than the saving justifies at this scale, and the fallout from
    /// over-evicting is one cold read.</para>
    /// </summary>
    public const string AllTag = "public";

    /// <summary>
    /// How stale a public page may get.
    ///
    /// <para>A minute absorbs the traffic this exists for — a link doing the rounds — and it is a
    /// backstop rather than the main control: every successful write evicts (see
    /// <see cref="EvictPublicCacheOnWriteFilter"/>), so this only bounds staleness from changes
    /// that never went through this API at all.</para>
    ///
    /// <para>Do not reach for "a GM would notice" as a reason to raise it. They would not: the
    /// Blazor host sends a bearer token on every call, and output caching refuses to serve a
    /// request carrying an <c>Authorization</c> header — so a signed-in GM always sees the live
    /// page, and never the copy strangers are getting.</para>
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Drops every cached public response. Called after every successful write, because the
    /// alternative is withdrawn content — a hidden source, a deleted note, a disabled world —
    /// still being served for up to <see cref="Duration"/>.
    /// </summary>
    public static ValueTask EvictAsync(IOutputCacheStore store, CancellationToken ct) =>
        store.EvictByTagAsync(AllTag, ct);
}
