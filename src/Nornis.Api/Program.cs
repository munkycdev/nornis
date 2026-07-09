using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Nornis.Api.Authentication;
using Nornis.Api.BackgroundServices;
using Nornis.Api.Filters;
using Nornis.Api.Middleware;
using Nornis.Api.Development;
using Nornis.Application.Ai;
using Nornis.Application.Application;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Messaging;
using Nornis.Application.Services;
using Nornis.Application.Validation;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Ai;
using Nornis.Infrastructure.Knowledge;
using Nornis.Infrastructure.Messaging;
using Nornis.Infrastructure.Persistence;
using Nornis.Infrastructure.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Authentication and authorization (Auth0 JWT with FallbackPolicy = RequireAuthenticatedUser)
if (builder.Environment.IsDevelopment() && builder.Configuration["Auth0:Domain"] == "your-tenant.auth0.com")
{
    // Dev mode: skip real Auth0 — DevAuthBypassMiddleware handles identity
    builder.Services.AddAuthentication().AddJwtBearer();
    builder.Services.AddAuthorization();
}
else
{
    builder.Services.AddAuth0Authentication(builder.Configuration);
}

// MVC controllers
builder.Services.AddControllers();

// Swagger/OpenAPI (Development only)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Nornis API",
            Version = "v1",
            Description = "Campaign knowledge management API for tabletop RPGs"
        });
    });
}

// Health checks
builder.Services.AddHealthChecks();

// DbContext registration (SQL Server)
builder.Services.AddDbContext<NornisDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repository registrations
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICampaignRepository, CampaignRepository>();
builder.Services.AddScoped<ICampaignMemberRepository, CampaignMemberRepository>();
builder.Services.AddScoped<ISourceRepository, SourceRepository>();
builder.Services.AddScoped<IReviewProposalRepository, ReviewProposalRepository>();
builder.Services.AddScoped<IReviewBatchRepository, ReviewBatchRepository>();
builder.Services.AddScoped<IArtifactRepository, ArtifactRepository>();
builder.Services.AddScoped<IArtifactFactRepository, ArtifactFactRepository>();
builder.Services.AddScoped<IArtifactRelationshipRepository, ArtifactRelationshipRepository>();
builder.Services.AddScoped<ISourceReferenceRepository, SourceReferenceRepository>();
builder.Services.AddScoped<IAiUsageRecordRepository, AiUsageRecordRepository>();
builder.Services.AddScoped<IHealthAssessmentRepository, HealthAssessmentRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();

// Application service registrations
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<ICampaignMemberService, CampaignMemberService>();
builder.Services.AddScoped<ISourceService, SourceService>();
builder.Services.AddScoped<IArtifactService, ArtifactService>();
builder.Services.AddScoped<ICanonService, CanonService>();
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<IContinuityAuditService, ContinuityAuditService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddSingleton<IProposalValidator, ProposalValidator>();
builder.Services.AddScoped<IProposalApplicator, ProposalApplicator>();
builder.Services.AddScoped<ICostService, CostService>();

// Loremaster service registrations
builder.Services.Configure<LoremasterOptions>(builder.Configuration.GetSection("Loremaster"));
builder.Services.Configure<AiBudgetOptions>(builder.Configuration.GetSection(AiBudgetOptions.SectionName));
builder.Services.AddScoped<IAiBudgetGuard, AiBudgetGuard>();
builder.Services.AddScoped<ILoremasterService, LoremasterService>();
builder.Services.AddScoped<ISuggestionService, SuggestionService>();
builder.Services.AddScoped<IKnowledgeRetriever, KeywordKnowledgeRetriever>();

// Continuity audit (AI-assessed health): options + hourly auto-trigger. The audit AI client
// reuses the Loremaster's Azure OpenAI ChatClient and configuration (registered below).
builder.Services.Configure<ContinuityAuditOptions>(builder.Configuration.GetSection("ContinuityAudit"));
builder.Services.AddHostedService<ContinuityAuditBackgroundService>();

var loremasterEndpoint = builder.Configuration["Loremaster:AiEndpoint"];
var loremasterModel = builder.Configuration["Loremaster:AiModel"];
if (!string.IsNullOrEmpty(loremasterEndpoint) && !loremasterEndpoint.Contains("<resource>"))
{
    var aiKey = builder.Configuration["Loremaster:AiKey"] ?? string.Empty;
    var openAiClient = new Azure.AI.OpenAI.AzureOpenAIClient(
        new Uri(loremasterEndpoint),
        new System.ClientModel.ApiKeyCredential(aiKey));
    builder.Services.AddSingleton(openAiClient.GetChatClient(loremasterModel ?? "gpt-4o"));
    builder.Services.AddScoped<ILoremasterAiClient, AzureOpenAiLoremasterClient>();
    builder.Services.AddScoped<IAuditAiClient, AzureOpenAiAuditClient>();
}
else
{
    // No Azure OpenAI configured — register stubs that throw on use
    builder.Services.AddScoped<ILoremasterAiClient>(sp =>
        throw new InvalidOperationException(
            "Azure OpenAI is not configured. Set 'Loremaster:AiEndpoint' and 'Loremaster:AiKey' in configuration to enable Ask the Loremaster."));
    builder.Services.AddScoped<IAuditAiClient>(sp =>
        throw new InvalidOperationException(
            "Azure OpenAI is not configured. Set 'Loremaster:AiEndpoint' and 'Loremaster:AiKey' in configuration to enable AI-assessed Continuity Health."));
}

// Azure Service Bus and extraction queue
var serviceBusConnectionString = builder.Configuration["AzureServiceBus:ConnectionString"];
if (!string.IsNullOrEmpty(serviceBusConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnectionString));
    builder.Services.AddSingleton<IExtractionQueueClient, ServiceBusExtractionQueueClient>();
}
else
{
    // In development without Service Bus, use a no-op client that logs instead of sending
    builder.Services.AddSingleton<IExtractionQueueClient, NoOpExtractionQueueClient>();
}

// MVC action filter for campaign-scoped endpoints
builder.Services.AddScoped<CampaignMemberActionFilter>();

var app = builder.Build();

// Middleware pipeline order:
// 1. Authentication (validates JWT)
// 2. Authorization (enforces policies, FallbackPolicy requires authenticated user)
// 3. User provisioning (resolves or creates Nornis User from JWT claims)
// 4. Endpoints (controllers, health checks)

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Nornis API v1");
        options.RoutePrefix = "swagger";
    });
}

if (app.Environment.IsDevelopment() && app.Configuration["Auth0:Domain"] == "your-tenant.auth0.com")
{
    // Dev mode: bypass auth entirely with a fake user
    app.UseMiddleware<UserProvisioningMiddleware>(); // skip real auth middleware
    app.UseMiddleware<DevAuthBypassMiddleware>();
}
else
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<UserProvisioningMiddleware>();
}

app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    AllowCachingResponses = false,
    ResponseWriter = WriteHealthResponse
}).AllowAnonymous();

app.Run();

static Task WriteHealthResponse(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";

    var status = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
        ? "Healthy"
        : "Unhealthy";

    context.Response.StatusCode = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;

    var json = System.Text.Json.JsonSerializer.Serialize(new { status });
    return context.Response.WriteAsync(json);
}

// Make Program accessible to integration tests via WebApplicationFactory<Program>
public partial class Program { }
