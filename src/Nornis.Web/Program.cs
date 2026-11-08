using System.Net;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.ResponseCompression;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Authentication;
using Nornis.Web.Components;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

// Web owns no dependencies, so its liveness endpoint is a constant answer; the path is named
// once because the mapping and the telemetry filter below both have to agree on it.
const string healthPath = "/health";

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
        })
        // Same reasoning as the API: the HTTP client instruments fan out per connection and
        // were the largest table in the workspace. Web's client is the typed one to the API.
        .WithMetrics(metrics => metrics.AddView("http.client.*", MetricStreamConfiguration.Drop));

    // The readiness probe's /health every ten seconds, dropped at the source — see the API.
    builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        options.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments(healthPath));
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
builder.Services.AddNornisDataProtection(builder.Configuration, signInEnabled: authEnabled);
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
                if (SignInLinking.IsLink(context.Properties))
                {
                    context.ProtocolMessage.Scope = SignInLinking.Scope;
                }
                return Task.CompletedTask;
            },
            // Linking another sign-in (feature 33) claims it here and keeps the session as it was.
            OnTokenValidated = SignInLinking.OnTokenValidatedAsync,
            OnRemoteFailure = context =>
            {
                // A link refused or cancelled at Auth0 goes back to Profile, not the error page.
                if (SignInLinking.IsLink(context.Properties))
                {
                    context.Response.Redirect(SignInLinking.OutcomeUrl("failed"));
                    context.HandleResponse();
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

    // Longer than the 100s default for one endpoint's sake: handwriting transcription is a
    // vision call over several photographed pages, and the API gives it 90s
    // (Transcription:TimeoutSeconds) before returning a 503 that tells the user to try
    // again. At the default the client would give up first and report "could not reach the
    // API" — the same wait ending in a worse sentence.
    client.Timeout = TimeSpan.FromSeconds(120);
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
builder.Services.AddScoped<Nornis.Web.State.AskHistoryStore>();
builder.Services.AddScoped<Nornis.Web.State.ViewAsState>();
builder.Services.AddScoped<Nornis.Web.State.AuthSessionState>();
builder.Services.AddScoped<Nornis.Web.State.GmNoteContext>();
builder.Services.AddScoped<Nornis.Web.State.BreadcrumbState>();
builder.Services.AddScoped<Nornis.Web.State.QuickSwitcherState>();
builder.Services.AddScoped<Nornis.Web.State.WorldMenuState>();
builder.Services.AddScoped<Nornis.Web.State.ThemeState>();
builder.Services.AddScoped<Nornis.Web.Services.GmNoteWriter>();

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

    // Profile › Link another sign-in (feature 33): the API issues the code with the session's
    // token, and the round trip through Auth0 carries it back inside the protected state.
    app.MapGet("/account/link-sign-in", async (HttpContext context, NornisApiClient api, CancellationToken ct) =>
        {
            var from = SignInLinking.Subject(context.User);
            var begun = await api.BeginSignInLinkAsync(ct);
            if (string.IsNullOrEmpty(from) || !begun.IsSuccess)
            {
                return Results.LocalRedirect(SignInLinking.OutcomeUrl("failed"));
            }

            return Results.Challenge(SignInLinking.Challenge(begun.Value!.Code, from),
                [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .RequireAuthorization();
}

app.UseAntiforgery();

// Liveness only. Web is a UI shell over the API and owns no dependencies of its own, so
// anything it could probe would be reporting on the API's behalf — which /status already
// does, better. This exists so the platform has something to ping.
app.MapGet(healthPath, () => Results.Json(new { status = "Healthy" })).AllowAnonymous();

// The root is the address a stranger types and also where members land: the Auth0 return URL
// and old bookmarks still say "/". Members go on to the dashboard, everyone
// else to the landing page, decided here rather than in a page component because a component
// prerenders the member shell for a stranger before its redirect can run, and an [Authorize]
// page sends them to the sign-in screen without ever showing what Nornis is.
app.MapGet("/", (HttpContext context) =>
        Results.LocalRedirect(context.User.Identity?.IsAuthenticated == true ? "/dashboard" : "/welcome"))
    .AllowAnonymous();

// Bibliotaph covers (feature 31) for <img> tags: the browser has a cookie, not a token, so this
// forwards with the member's token and lets the browser keep the image for a day.
var covers = app.MapGet("/library-covers/{worldId:guid}/{documentId:guid}", async (
        Guid worldId, Guid documentId, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    {
        var cover = await api.GetLibraryCoverAsync(worldId, documentId, ct);
        if (cover is not { } found)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.File(found.Bytes, found.ContentType);
    });
if (authEnabled)
{
    covers.RequireAuthorization();
}

// The Prep booklet (feature 34) for the print page's frame and its Download link. The browser
// has a cookie, not a token, so this forwards with the member's token. Never cached: the
// booklet changes with every edit. "download=1" saves it rather than showing it.
var booklets = app.MapGet("/prep-booklets/{worldId:guid}/{adventureId:guid}", async (
        Guid worldId, Guid adventureId, bool? download, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    {
        var booklet = await api.GetAdventureBookletAsync(worldId, adventureId, ct);
        if (booklet is not { } found)
        {
            return Results.NotFound();
        }

        var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(download == true ? "attachment" : "inline");
        disposition.SetHttpFileName(found.FileName ?? "booklet.pdf");
        context.Response.Headers.ContentDisposition = disposition.ToString();
        context.Response.Headers.CacheControl = "private, no-store";
        return Results.File(found.Bytes, "application/pdf");
    });
if (authEnabled)
{
    booklets.RequireAuthorization();
}

// The props sheet (feature 38): every prop box on its own page, always downloaded. The print page
// offers it only when the Adventure has a prop box; a stale page that asks anyway gets a sentence.
var props = app.MapGet("/prep-props/{worldId:guid}/{adventureId:guid}", async (
        Guid worldId, Guid adventureId, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    {
        var sheet = await api.GetAdventurePropsAsync(worldId, adventureId, ct);
        if (sheet is not { } found)
        {
            return Results.Text("This adventure has no prop boxes to print, or it couldn't be printed just now.", statusCode: StatusCodes.Status404NotFound);
        }

        var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(found.FileName ?? "props.pdf");
        context.Response.Headers.ContentDisposition = disposition.ToString();
        context.Response.Headers.CacheControl = "private, no-store";
        return Results.File(found.Bytes, "application/pdf");
    });
if (authEnabled)
{
    props.RequireAuthorization();
}

// Prep cover art (feature 38): the chosen cover and the generated options, for the Cover card.
// The cover's URL carries its stamp and an option never changes, so both may be cached.
var prepCovers = app.MapGroup("/prep-covers/{worldId:guid}/{adventureId:guid}");
prepCovers.MapGet("", async (Guid worldId, Guid adventureId, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    {
        var image = await api.GetCoverImageAsync(worldId, adventureId, ct);
        return CoverImage(image, context);
    });
prepCovers.MapGet("/options/{optionId:guid}", async (
        Guid worldId, Guid adventureId, Guid optionId, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    {
        var image = await api.GetCoverOptionAsync(worldId, adventureId, optionId, ct);
        return CoverImage(image, context);
    });
if (authEnabled)
{
    prepCovers.RequireAuthorization();
}

// A world's key art (feature 42): the art for any member (the API checks membership), and the
// generated options for the GM's card. The art's URL carries its stamp and an option never
// changes, so both may be cached.
var worldArt = app.MapGroup("/world-art/{worldId:guid}");
worldArt.MapGet("", async (Guid worldId, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    CoverImage(await api.GetKeyArtImageAsync(worldId, ct), context));
worldArt.MapGet("/options/{optionId:guid}", async (Guid worldId, Guid optionId, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    CoverImage(await api.GetKeyArtOptionAsync(worldId, optionId, ct), context));
if (authEnabled)
{
    worldArt.RequireAuthorization();
}

// A public world's key art, anonymously: the API serves it only while public access is on.
app.MapGet("/w/{slug}/art", async (string slug, NornisApiClient api, HttpContext context, CancellationToken ct) =>
    CoverImage(await api.GetPublicKeyArtImageAsync(slug, ct), context, shared: true))
    .AllowAnonymous();

static IResult CoverImage((byte[] Bytes, string ContentType)? image, HttpContext context, bool shared = false)
{
    if (image is not { } found)
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = shared ? "public, max-age=86400" : "private, max-age=86400";
    return Results.File(found.Bytes, found.ContentType);
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
