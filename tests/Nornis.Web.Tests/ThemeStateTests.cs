using Microsoft.JSInterop;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The theme preference lives in the browser. These pin the resolution rule — an explicit
/// choice beats the device, and only the system choice follows it — and that a choice is
/// written where the next visit will find it.
/// </summary>
[TestFixture]
public class ThemeStateTests
{
    [TestCase(null, ThemePreference.System)]
    [TestCase("", ThemePreference.System)]
    [TestCase("nonsense", ThemePreference.System)]
    [TestCase("light", ThemePreference.Light)]
    [TestCase(" Dark ", ThemePreference.Dark)]
    public void Parse_IsForgiving(string? raw, ThemePreference expected) =>
        Assert.That(ThemeState.Parse(raw), Is.EqualTo(expected));

    [Test]
    public void SystemPreference_FollowsTheDevice()
    {
        var state = new ThemeState(new FakeJs());
        var changes = 0;
        state.Changed += () => changes++;

        Assert.That(state.IsDark, Is.False, "a device that said nothing yet reads light");
        state.SetSystem(true);
        Assert.That(state.IsDark, Is.True);
        Assert.That(changes, Is.EqualTo(1));

        state.SetSystem(true);
        Assert.That(changes, Is.EqualTo(1), "no change, no event");
    }

    [Test]
    public async Task ExplicitChoice_IgnoresTheDevice()
    {
        var js = new FakeJs();
        var state = new ThemeState(js);
        state.SetSystem(true);

        await state.SetPreferenceAsync(ThemePreference.Light);

        Assert.Multiple(() =>
        {
            Assert.That(state.IsDark, Is.False, "light was chosen on a dark device");
            Assert.That(js.Stored[ThemeState.StorageKey], Is.EqualTo("light"));
        });

        var changes = 0;
        state.Changed += () => changes++;
        state.SetSystem(false);
        Assert.That(changes, Is.Zero, "the device changing is not news while a choice stands");
    }

    [Test]
    public async Task SavedChoice_IsRestored()
    {
        var js = new FakeJs();
        js.Stored[ThemeState.StorageKey] = "dark";
        var state = new ThemeState(js);

        await state.EnsureLoadedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(state.Preference, Is.EqualTo(ThemePreference.Dark));
            Assert.That(state.IsDark, Is.True);
        });
    }

    [Test]
    public async Task NoStorage_FallsBackToTheDevice()
    {
        var state = new ThemeState(new FakeJs { Throws = true });
        state.SetSystem(true);

        await state.EnsureLoadedAsync();
        await state.SetPreferenceAsync(ThemePreference.Light);

        Assert.Multiple(() =>
        {
            Assert.That(state.Preference, Is.EqualTo(ThemePreference.Light), "honoured for the session even when it cannot be saved");
            Assert.That(state.IsDark, Is.False);
        });
    }

    private sealed class FakeJs : IJSRuntime
    {
        public Dictionary<string, string> Stored { get; } = [];
        public bool Throws { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (Throws)
            {
                throw new JSException("no storage");
            }

            var key = (string)args![0]!;
            switch (identifier)
            {
                case "localStorage.getItem":
                    return ValueTask.FromResult((TValue)(object?)Stored.GetValueOrDefault(key)!);
                case "localStorage.setItem":
                    Stored[key] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                default:
                    throw new InvalidOperationException(identifier);
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
