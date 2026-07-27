using System.Net;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.ResponseCompression;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Authentication;
using Nornis.Web.Components;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Observability: Azure Monitor via OpenTelemetry — active only when the deployment
// provides a connection string; local runs and tests emit nothing.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("nornis-web"))
        .UseAzureMonitor(options =>
        {
            // See the note in Nornis.Api/Program.cs. The Web app additionally emits a record per
            // static-file request, so it is the noisiest of the three.
            options.SamplingRatio = builder.Configuration.GetValue<float?>("Telemetry:SamplingRatio") ?? 0.10f;
            options.EnableTraceBasedLogsSampler = true;
        });
}

// Blazor Web App with interactive server rendering (per architecture decision).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Compresses the Blazor Server payloads and any dynamically-served content. Static assets are
// handled separately by MapStaticAssets, which serves precompressed files built at publish time
// rather than compressing per request.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

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
        // Access tokens live 24h but are silently renewed via the offline_access
        // refresh token (Auth0TokenRefresher) — the cookie itself slides for a month
        // of activity. Expired-and-unrefreshable tokens reject the principal, so a
        // long-lived cookie can never outlive its ability to call the API.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/welcome";
        options.Events.OnValidatePrincipal = context =>
            context.HttpContext.RequestServices
                .GetRequiredService<Auth0TokenRefresher>()
                .ValidatePrincipalAsync(context);
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
        options.Scope.Add("offline_access"); // refresh token — sessions outlive the 24h access token

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
else
{
    // Without Auth0, [Authorize] page metadata still engages the framework's
    // authorization machinery — give it a scheme that signs everyone in locally.
    builder.Services.AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthenticationHandler>(
            DevAuthenticationHandler.SchemeName, null);
    builder.Services.AddAuthorization();
}

// Silent Auth0 token renewal (no-op passthrough when auth is disabled locally).
builder.Services.AddSingleton<Auth0TokenRefresher>();
builder.Services.AddHttpClient(nameof(Auth0TokenRefresher));

// Typed HTTP client to nornis-api. Web never touches the database directly.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5000";
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<NornisApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
    // The default handler sets AutomaticDecompression to None, so this client never sent
    // Accept-Encoding and the API's compression would have been dead weight on this leg.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    })
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped<Nornis.Web.State.WorldState>();
builder.Services.AddScoped<Nornis.Web.State.ActivitySignal>();
builder.Services.AddScoped<Nornis.Web.State.OnboardingState>();
builder.Services.AddScoped<Nornis.Web.State.AskState>();
builder.Services.AddScoped<Nornis.Web.State.ViewAsState>();

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
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseResponseCompression();

app.UseHttpsRedirection();

// MapStaticAssets replaces UseStaticFiles: it serves the .br/.gz variants produced at publish
// time, and emits fingerprinted URLs with immutable Cache-Control and ETags. That removes both
// the per-request compression cost and the hand-rolled "?v=" cache busters in App.razor.
app.MapStaticAssets();

app.UseAuthentication();
app.UseAuthorization();

if (authEnabled)
{
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
