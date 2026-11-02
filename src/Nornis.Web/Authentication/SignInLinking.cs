using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Authentication;

/// <summary>
/// The Web's half of linking another sign-in (feature 33). The API decides everything; this
/// carries its one-time code through an Auth0 round trip that forces the login screen, then
/// claims it with the new sign-in's own access token. The session the member started from is
/// kept: the callback never signs the new identity in.
/// </summary>
public static class SignInLinking
{
    /// <summary>The API's link code, in the challenge's protected properties.</summary>
    public const string CodeItem = "nornis.link-code";

    /// <summary>The session's subject when the link began; the callback must come back to it.</summary>
    public const string FromItem = "nornis.link-from";

    /// <summary>A link asks for an ID and an access token only: no refresh token nobody keeps.</summary>
    public const string Scope = "openid profile email";

    public static bool IsLink(AuthenticationProperties? properties) =>
        properties?.Items.ContainsKey(CodeItem) == true;

    /// <summary>Where Profile shows how the link ended.</summary>
    public static string OutcomeUrl(string outcome) => $"/profile?signin={outcome}";

    /// <summary>The <c>?signin=</c> word for a claim's answer.</summary>
    public static string Outcome(ApiResult<SignInClaimDto> result) =>
        result.IsSuccess
            ? result.Value!.Outcome
            : result.Error!.Code switch
            {
                "sign_in_taken" => "taken",
                "link_expired" => "expired",
                _ => "failed",
            };

    /// <summary>The challenge that begins a link, for the session whose subject is <paramref name="from"/>.</summary>
    public static OpenIdConnectChallengeProperties Challenge(string code, string from)
    {
        var properties = new OpenIdConnectChallengeProperties
        {
            RedirectUri = "/profile",
            // Without it Auth0 hands back the session already in use.
            Prompt = "login",
        };
        properties.Items[CodeItem] = code;
        properties.Items[FromItem] = from;
        return properties;
    }

    public static string? Subject(ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue("sub");

    /// <summary>
    /// <c>OnTokenValidated</c> for a link: claims it and answers with a redirect to Profile, so
    /// the new identity is never signed in. Any other sign-in passes through untouched.
    /// </summary>
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        if (!IsLink(context.Properties))
        {
            return;
        }

        var outcome = await ClaimAsync(context);
        context.Response.Redirect(OutcomeUrl(outcome));
        context.HandleResponse();
    }

    private static async Task<string> ClaimAsync(TokenValidatedContext context)
    {
        var items = context.Properties!.Items;
        items.TryGetValue(CodeItem, out var code);
        items.TryGetValue(FromItem, out var from);

        // The browser must still be signed in as the member who started the link. A callback
        // that lands in another session, or none, changes nothing.
        var session = await context.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!session.Succeeded
            || string.IsNullOrEmpty(from)
            || !string.Equals(Subject(session.Principal), from, StringComparison.Ordinal))
        {
            return "failed";
        }

        var accessToken = context.TokenEndpointResponse?.AccessToken;
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(code))
        {
            return "failed";
        }

        var api = context.HttpContext.RequestServices.GetRequiredService<NornisApiClient>();
        return Outcome(await api.ClaimSignInAsync(accessToken, code, context.HttpContext.RequestAborted));
    }
}
