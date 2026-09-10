namespace Nornis.Web.State;

/// <summary>
/// Opens the quick switcher from anywhere — the sidebar's search box, the Ctrl+K listener in
/// the layout — without those callers knowing where the dialog lives.
/// </summary>
public sealed class QuickSwitcherState
{
    public event Action? OpenRequested;

    public void Open() => OpenRequested?.Invoke();
}

/// <summary>
/// Opens the world switcher (which lives in the sidebar) from the breadcrumb's world crumb.
/// </summary>
public sealed class WorldMenuState
{
    public event Action? OpenRequested;

    public void Open() => OpenRequested?.Invoke();
}
