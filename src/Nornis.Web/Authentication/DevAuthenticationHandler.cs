using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Nornis.Web.Authentication;

/// <summary>
/// Local-development scheme used when Auth0 is not configured: every request is a
/// signed-in "local-dev" user, so [Authorize] pages work against the API's dev-auth
/// bypass. Never registered when Auth0 is configured.
/// </summary>
public class DevAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevLocal";

    public DevAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "dev|local-testing-user"),
            new Claim("name", "local-dev")
        ], SchemeName, nameType: "name", roleType: ClaimsIdentity.DefaultRoleClaimType);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
