using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Application.Bibliotaph;

namespace Nornis.Api.Controllers;

/// <summary>
/// Profile › Bibliotaph (feature 31, Requirement 1): the signed-in user's own connection. Not
/// world-scoped: a connection belongs to a person, and a world uses its creator's.
/// </summary>
[ApiController]
[Route("api/bibliotaph/connection")]
public class BibliotaphConnectionController : ControllerBase
{
    private readonly IBibliotaphConnectService _connect;

    public BibliotaphConnectionController(IBibliotaphConnectService connect)
    {
        _connect = connect;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var view = await _connect.GetAsync(HttpContext.GetNornisUser().Id, ct);
        return Ok(new BibliotaphConnectionResponse(view.Configured, view.Status, view.ConnectedAt, view.LastUsedAt));
    }

    [HttpPost("begin")]
    public async Task<IActionResult> Begin(CancellationToken ct)
    {
        var result = await _connect.BeginAsync(HttpContext.GetNornisUser().Id, ct);
        return result.IsSuccess ? Ok(new BibliotaphRedirectResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpPost("authorize")]
    public IActionResult Authorize([FromBody] BibliotaphAuthorizeRequest request)
    {
        var result = _connect.AuthorizeUrl(HttpContext.GetNornisUser().Id, request.State);
        return result.IsSuccess ? Ok(new BibliotaphRedirectResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var result = await _connect.DisconnectAsync(HttpContext.GetNornisUser().Id, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }
}

/// <summary>
/// Where Auth0 sends the browser after the GM consents. Anonymous because it is a browser
/// redirect with no bearer; the protected state names the user, and the ID token's subject must
/// be one of that user's sign-ins, so a stray or replayed callback can attach nothing. It sets no cookie and
/// answers only with a redirect back to Nornis.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("bibliotaph/callback")]
public class BibliotaphCallbackController : ControllerBase
{
    private readonly IBibliotaphConnectService _connect;

    public BibliotaphCallbackController(IBibliotaphConnectService connect)
    {
        _connect = connect;
    }

    [HttpGet]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct) =>
        Redirect(await _connect.CompleteAsync(code, state, error, ct));
}
