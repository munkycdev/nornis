using Microsoft.JSInterop;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// What a JS call does when there is no browser left to make it in (3.35). A call made only for
/// its effect in the browser reports that the circuit has gone instead of throwing; anything
/// else that goes wrong still throws; and a copy the browser refuses is reported, not claimed.
/// </summary>
[TestFixture]
public class JsRuntimeExtensionsTests
{
    [Test]
    public async Task ACallThatReachesTheBrowser_IsMade_AndSaysSo()
    {
        var js = new FakeJs();

        var made = await js.TryInvokeVoidAsync("nornisUpload.release", "input-1");

        Assert.Multiple(() =>
        {
            Assert.That(made, Is.True);
            Assert.That(js.Calls, Is.EqualTo((string[])["nornisUpload.release(input-1)"]));
        });
    }

    [Test]
    public async Task WhenTheCircuitHasGone_TheCallIsNotAnError()
    {
        var js = new FakeJs { Throws = new JSDisconnectedException("the circuit has disconnected") };

        var made = await js.TryInvokeVoidAsync("nornisUpload.release", "input-1");

        Assert.That(made, Is.False);
    }

    [Test]
    public void AnyOtherFailure_StillThrows()
    {
        // Only "nobody is there" is forgiven. A script that throws is a bug to hear about.
        var js = new FakeJs { Throws = new JSException("nornisUpload is not defined") };

        Assert.That(async () => await js.TryInvokeVoidAsync("nornisUpload.release", "input-1"),
            Throws.TypeOf<JSException>());
    }

    [Test]
    public async Task ACopyTheBrowserAllows_IsMade()
    {
        var js = new FakeJs();

        var copied = await js.TryCopyTextAsync("https://nornis.app/invite/abc");

        Assert.Multiple(() =>
        {
            Assert.That(copied, Is.True);
            Assert.That(js.Calls, Is.EqualTo((string[])["navigator.clipboard.writeText(https://nornis.app/invite/abc)"]));
        });
    }

    [Test]
    public async Task ACopyTheBrowserRefuses_IsReported_NotThrown()
    {
        var js = new FakeJs { Throws = new JSException("NotAllowedError: Write permission denied.") };

        Assert.That(await js.TryCopyTextAsync("https://nornis.app/invite/abc"), Is.False);
    }

    [Test]
    public async Task ACopyWithTheCircuitGone_IsReported_NotThrown()
    {
        // JSDisconnectedException is not a JSException, so it needs its own mention.
        var js = new FakeJs { Throws = new JSDisconnectedException("the circuit has disconnected") };

        Assert.That(await js.TryCopyTextAsync("https://nornis.app/invite/abc"), Is.False);
    }

    private sealed class FakeJs : IJSRuntime
    {
        public Exception? Throws { get; init; }
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            Calls.Add($"{identifier}({string.Join(", ", args ?? [])})");
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
