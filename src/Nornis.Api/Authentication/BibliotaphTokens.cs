using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;

namespace Nornis.Api.Authentication;

/// <summary>
/// Telling a token Bibliotaph holds from one the Nornis Web holds (feature 31). Both are issued
/// by the same tenant for the same audience, so audience can't separate them; the authorized
/// party (<c>azp</c>) can. Bibliotaph's application may reach <c>api/bibliotaph/v1</c> and
/// nothing else, and nothing else may reach that.
/// </summary>
public static class BibliotaphTokens
{
    /// <summary>The policy the five provider endpoints use.</summary>
    public const string PolicyName = "Bibliotaph";

    public const string RequiredScope = "published:read";

    /// <summary>The application a token was issued to: <c>azp</c>, or <c>client_id</c> where a tenant sends that.</summary>
    public static string? ClientIdOf(ClaimsPrincipal user) =>
        user.FindFirstValue("azp") ?? user.FindFirstValue("client_id");

    public static bool IsFromBibliotaph(ClaimsPrincipal user, string? providerClientId) =>
        !string.IsNullOrWhiteSpace(providerClientId)
        && string.Equals(ClientIdOf(user), providerClientId, StringComparison.Ordinal);

    /// <summary>Whether the space-separated <c>scope</c> claim (Auth0's form) holds <paramref name="scope"/>.</summary>
    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    /// <summary>
    /// The default policy, which is also the fallback: an authenticated user, and not Bibliotaph's
    /// application. The client id is read per request from options, so the rule needs no restart
    /// to follow configuration and is off while <c>Bibliotaph:ProviderClientId</c> is unset.
    /// </summary>
    public static AuthorizationPolicy DefaultPolicy() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => !IsFromBibliotaph(ctx.User, ProviderClientId(ctx)))
            .Build();

    /// <summary>Bibliotaph's application, with <see cref="RequiredScope"/>. Never the Web's token.</summary>
    public static AuthorizationPolicy BibliotaphPolicy() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => IsFromBibliotaph(ctx.User, ProviderClientId(ctx)) && HasScope(ctx.User, RequiredScope))
            .Build();

    public static void AddNornisPolicies(this AuthorizationOptions options)
    {
        options.DefaultPolicy = DefaultPolicy();
        options.FallbackPolicy = options.DefaultPolicy;
        options.AddPolicy(PolicyName, BibliotaphPolicy());
    }

    private static string? ProviderClientId(AuthorizationHandlerContext ctx)
    {
        var services = ctx.Resource switch
        {
            HttpContext http => http.RequestServices,
            Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext filter => filter.HttpContext.RequestServices,
            _ => null,
        };

        return services?.GetService<IOptions<BibliotaphOptions>>()?.Value.ProviderClientId;
    }
}
