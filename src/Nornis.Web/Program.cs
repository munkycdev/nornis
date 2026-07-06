using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App with interactive server rendering (per architecture decision).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Typed HTTP client to nornis-api. Web never touches the database directly.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5000";
builder.Services.AddHttpClient<NornisApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddScoped<Nornis.Web.State.CampaignState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
