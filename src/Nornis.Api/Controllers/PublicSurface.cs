using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// What the public world shows, over and above visibility. Visibility says who may read a
/// thing; this says which of the readable things belong on a site for strangers. Every public
/// read of a source — the list, a source by id, its knowledge and locations, a campaign's
/// sessions, an entry's citations — asks here, so the answer cannot differ between them.
/// </summary>
public static class PublicSurface
{
    /// <summary>
    /// A reveal record is party-visible on purpose — it is the GM's note to the table about
    /// what was just disclosed — and public on no account: it is addressed to the players, not
    /// to whoever holds the link. The rule is about the audience, not the scope, which is why
    /// it lives on the surface and not in <c>SourceVisibilityRule</c>.
    /// </summary>
    public static bool ShowsSource(SourceType type) => type != SourceType.Reveal;
}
