using Microsoft.JSInterop;

namespace Nornis.Web.Services;

/// <summary>
/// The one place that says what a JS call does when there is no browser left to make it in.
///
/// A Blazor Server circuit can end while a handler is still running: the tab closes during an
/// upload, or the reader leaves the page between a render and its <c>OnAfterRenderAsync</c>.
/// The next JS call then throws <see cref="JSDisconnectedException"/>. For a call that only
/// acts on the browser (set up a chart, release a file input, open a tab) that is not a
/// failure, because the thing it would have acted on went with the tab.
///
/// These are for those calls. A call whose answer the code goes on to use (the editor's text,
/// whether an upload arrived) is deliberately not here: with nobody to answer, the handler has
/// to stop, and the exception is what stops it. Handing back a default instead would make
/// "the tab closed" look like "the notes are empty".
/// </summary>
public static class JsRuntimeExtensions
{
    /// <summary>
    /// Makes a JS call for its effect in the browser. False when the circuit has gone and there
    /// was no browser to make it in; any other failure still throws.
    /// </summary>
    public static async ValueTask<bool> TryInvokeVoidAsync(this IJSRuntime js, string identifier, params object?[]? args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Puts text on the clipboard. False when the browser refuses (permission denied, or a page
    /// that is not served over HTTPS has no clipboard at all) or the circuit has gone, so the
    /// caller can say so instead of claiming a copy that did not happen.
    /// </summary>
    public static async ValueTask<bool> TryCopyTextAsync(this IJSRuntime js, string text)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
            return true;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            return false;
        }
    }
}
