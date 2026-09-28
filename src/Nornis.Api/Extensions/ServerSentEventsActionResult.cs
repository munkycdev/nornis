using System.Net.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;

namespace Nornis.Api.Extensions;

/// <summary>
/// Lets a controller action answer with a server-sent event stream while its failure paths stay
/// ordinary <see cref="IActionResult"/>s. The framework's SSE result is a minimal-API
/// <see cref="IResult"/>, which an action typed to return <see cref="IActionResult"/> cannot
/// hand back directly; this is the adapter, and it adds nothing to what that result does —
/// unbuffered writes, no-cache headers, and the request's abort token on the enumeration.
/// </summary>
public sealed class ServerSentEventsActionResult : IActionResult
{
    private readonly IAsyncEnumerable<SseItem<string>> _events;

    public ServerSentEventsActionResult(IAsyncEnumerable<SseItem<string>> events)
    {
        _events = events;
    }

    public Task ExecuteResultAsync(ActionContext context) =>
        TypedResults.ServerSentEvents(_events).ExecuteAsync(context.HttpContext);
}
