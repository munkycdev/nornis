using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Nornis.Api.Authentication;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Middleware;
using Nornis.Application.Accounts;

namespace Nornis.Api.Controllers;

/// <summary>
/// Profile › Sign-ins (feature 33): the ways the signed-in user can sign in. Not world-scoped.
/// The rules are <see cref="SignInService"/>'s; this passes in the caller and the token's subject.
/// </summary>
[ApiController]
[Route("api/me/sign-ins")]
public class SignInsController : ControllerBase
{
    private readonly ISignInService _signIns;
    private readonly IMemoryCache _cache;

    public SignInsController(ISignInService signIns, IMemoryCache cache)
    {
        _signIns = signIns;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var views = await _signIns.ListAsync(HttpContext.GetNornisUser().Id, Subject(), ct);
        return Ok(views.Select(v => new SignInResponse(v.Id, v.Provider, v.AddedAt, v.Current)));
    }

    [HttpPost("link")]
    public async Task<IActionResult> Link(CancellationToken ct) =>
        Ok(new SignInLinkResponse(await _signIns.BeginLinkAsync(HttpContext.GetNornisUser().Id, ct)));

    /// <summary>
    /// Called with the new sign-in's own token, whose subject has no user yet, so provisioning is
    /// skipped and there is no Nornis user on this request: the code says whose account it joins.
    /// </summary>
    [HttpPost("claim")]
    [SkipUserProvisioning]
    public async Task<IActionResult> Claim([FromBody] ClaimSignInRequest request, CancellationToken ct)
    {
        var result = await _signIns.ClaimAsync(request.Code, Subject(), ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(new SignInClaimResponse(result.Value == SignInLinkOutcome.Linked ? "linked" : "already-linked"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var result = await _signIns.RemoveAsync(HttpContext.GetNornisUser().Id, id, Subject(), ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        // This replica stops resolving the subject at once; others within the cache's lifetime.
        _cache.Remove(UserProvisioningMiddleware.CacheKey(result.Value!));
        return NoContent();
    }

    private string Subject() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
}
