using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Authentication;
using Nornis.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App with interactive server rendering (per architecture decision).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Auth0 login is enabled when the deployment provides a ClientId; locally the app
// runs anonymous against the API's dev-auth bypass.
var auth0 = builder.Configuration.GetSection("Auth0");
var authEnabled = !string.IsNullOrEmpty(auth0["ClientId"]);
builder.Services.AddSingleton(new AuthFeature(authEnabled));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

if (authEnabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        // Auth0 access tokens for the Nornis API live 24h; keep the cookie aligned so
        // a stale cookie cannot outlive its ability to call the API.
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = false;
        options.AccessDeniedPath = "/welcome";
    })
    .AddOpenIdConnect(options =>
    {
        var domain = auth0["Domain"]!;
        options.Authority = $"https://{domain}";
        options.ClientId = auth0["ClientId"]!;
        options.ClientSecret = auth0["ClientSecret"];

        options.ResponseType = "code";
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";

        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.TokenValidationParameters.NameClaimType = "name";

        var audience = auth0["Audience"];
        options.Events = new OpenIdConnectEvents
        {
            // Request an access token for the Nornis API, not just an id token.
            OnRedirectToIdentityProvider = context =>
            {
                if (!string.IsNullOrEmpty(audience))
                {
                    context.ProtocolMessage.SetParameter("audience", audience);
                }
                return Task.CompletedTask;
            },
            // Auth0 requires its own logout endpoint to end the Auth0 session.
            OnRedirectToIdentityProviderForSignOut = context =>
            {
                var returnTo = Uri.EscapeDataString($"{context.Request.Scheme}://{context.Request.Host}/welcome");
                context.Response.Redirect($"https://{domain}/v2/logout?client_id={auth0["ClientId"]}&returnTo={returnTo}");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization();
}

// Typed HTTP client to nornis-api. Web never touches the database directly.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5000";
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<NornisApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
}).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped<Nornis.Web.State.WorldState>();
builder.Services.AddScoped<Nornis.Web.State.AskState>();

var app = builder.Build();

// Container Apps terminates TLS at ingress; honor X-Forwarded-Proto/For so OIDC
// redirect URIs (and any absolute URL generation) use https and the real host.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
};
// The ingress proxy is not a fixed address; clearing the loopback-only defaults is
// required or the headers are silently ignored ({ } in an initializer clears nothing).
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/account/login", (string? returnUrl) =>
        Results.Challenge(new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl
        }, [OpenIdConnectDefaults.AuthenticationScheme]));

    app.MapGet("/account/logout", () =>
        Results.SignOut(new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/welcome" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
}

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
