using Microsoft.JSInterop;

namespace Nornis.Web.State;

/// <summary>Light, dark, or whatever the device says.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Which of the two palettes the member reads in. The preference lives in the browser
/// (localStorage, per device) rather than on the User row: a theme belongs to the screen it is
/// read on, and a lamp-lit laptop and a daylight phone can reasonably differ. Public pages
/// never read it — they follow the device alone.
///
/// The layout reports the device's own preference in through <see cref="SetSystem"/>, and
/// <see cref="IsDark"/> is the resolved answer the theme provider binds to.
/// </summary>
public sealed class ThemeState(IJSRuntime js)
{
    /// <summary>localStorage key: "light", "dark" or "system".</summary>
    public const string StorageKey = "nornis:theme";

    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    /// <summary>What the device asked for, as last reported by the layout.</summary>
    public bool SystemIsDark { get; private set; }

    public bool IsDark => Preference switch
    {
        ThemePreference.Light => false,
        ThemePreference.Dark => true,
        _ => SystemIsDark,
    };

    /// <summary>Raised whenever <see cref="IsDark"/> or <see cref="Preference"/> may have changed.</summary>
    public event Action? Changed;

    private bool _loaded;

    /// <summary>Reads the saved preference once. Needs JS, so the layout calls it after its first interactive render.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            var parsed = Parse(raw);
            if (parsed != Preference)
            {
                Preference = parsed;
                Changed?.Invoke();
            }
        }
        catch (JSException)
        {
            // No storage: the device's preference it is.
        }
    }

    public async Task SetPreferenceAsync(ThemePreference preference)
    {
        if (preference == Preference)
        {
            return;
        }

        Preference = preference;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, Serialize(preference));
        }
        catch (JSException)
        {
            // Not persisted, but honoured for this session.
        }

        Changed?.Invoke();
    }

    /// <summary>The layout reports the device's preference, at boot and whenever it changes.</summary>
    public void SetSystem(bool dark)
    {
        if (dark == SystemIsDark)
        {
            return;
        }

        SystemIsDark = dark;
        if (Preference == ThemePreference.System)
        {
            Changed?.Invoke();
        }
    }

    public static ThemePreference Parse(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "light" => ThemePreference.Light,
        "dark" => ThemePreference.Dark,
        _ => ThemePreference.System,
    };

    public static string Serialize(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => "system",
    };
}
