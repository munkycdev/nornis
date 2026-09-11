using Nornis.Web.Navigation;

namespace Nornis.Web.State;

/// <summary>One crumb: a label and, for every crumb but usually the last, where it leads.</summary>
public sealed record Crumb(string Label, string? Href = null);

/// <summary>
/// Where the reader is, in the world's own structure — the one place the breadcrumb is
/// decided. The layout renders it; pages set it.
///
/// Two layers so every page has a trail without every page having to write one. On each
/// navigation the layout sets the default from the route (<see cref="NavGroups.FindByPath"/>:
/// "Codex" for anything under <c>/codex</c>). A page that knows more — an artifact's name
/// and type, a session's campaign — replaces it from data it already loaded, per Requirement
/// 2.3: a breadcrumb never costs a request. The world crumb is always first and is supplied by
/// the layout, so a page's trail starts one level in.
/// </summary>
public sealed class BreadcrumbState
{
    private IReadOnlyList<Crumb> _trail = [];
    private string _trailPath = string.Empty;

    public IReadOnlyList<Crumb> Trail => _trail;

    public event Action? Changed;

    /// <summary>
    /// The route default. Called by the layout before the new page renders, and only replaces
    /// a trail set for a <em>different</em> path, so a page that has already set its own for
    /// this URL is not overwritten by a late layout event.
    /// </summary>
    public void SetFromRoute(string path)
    {
        var normalized = "/" + path.Trim('/');
        if (_trailPath == normalized)
        {
            return;
        }

        var item = NavGroups.FindByPath(normalized);
        _trailPath = normalized;
        _trail = item is null ? [] : [new Crumb(item.Label, item.Href)];
        Changed?.Invoke();
    }

    /// <summary>A page's own trail for its current path, from data it already holds.</summary>
    public void Set(string path, params Crumb[] trail)
    {
        _trailPath = "/" + path.Trim('/');
        _trail = trail;
        Changed?.Invoke();
    }
}
