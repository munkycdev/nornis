using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nornis.Application.Messaging;
using Nornis.Infrastructure.Persistence;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that replaces the SQL Server database with an in-memory
/// provider and overrides Auth0 JWT authentication with a test scheme that validates
/// tokens from the TestJwtIssuer. Also registers a FakeExtractionQueueClient for
/// asserting on extraction messages sent during source operations.
/// </summary>
public class NornisWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly FakeExtractionQueueClient _fakeExtractionQueueClient = new();

    /// <summary>
    /// The fake extraction queue client used by this factory instance.
    /// Tests can use this to assert on messages sent or configure failures.
    /// </summary>
    public FakeExtractionQueueClient ExtractionQueueClient => _fakeExtractionQueueClient;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<NornisDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Remove the existing DbContext service itself if registered directly
            var dbContextServiceDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(NornisDbContext));
            if (dbContextServiceDescriptor is not null)
            {
                services.Remove(dbContextServiceDescriptor);
            }

            // Add in-memory database with transaction warning suppressed
            services.AddDbContext<NornisDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.ConfigureWarnings(w =>
                    w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            // Replace IExtractionQueueClient with the fake for integration tests
            var queueDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IExtractionQueueClient));
            if (queueDescriptor is not null)
            {
                services.Remove(queueDescriptor);
            }

            services.AddSingleton<IExtractionQueueClient>(_fakeExtractionQueueClient);

            // The DI stub for IRetrospectiveAiClient throws at resolution when Azure
            // OpenAI is unconfigured, which would break controller activation for every
            // Storylines endpoint. Replace it with a benign fake.
            var retroDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(Nornis.Application.Ai.IRetrospectiveAiClient));
            if (retroDescriptor is not null)
            {
                services.Remove(retroDescriptor);
            }
            services.AddScoped<Nornis.Application.Ai.IRetrospectiveAiClient, FakeRetrospectiveAiClient>();

            // Override JWT Bearer authentication to validate against the test issuer
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestJwtIssuer.Issuer,
                    ValidateAudience = true,
                    ValidAudience = TestJwtIssuer.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = TestJwtIssuer.GetSecurityKey(),
                    ClockSkew = TimeSpan.Zero
                };
            });
        });

        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Creates an HttpClient preconfigured with a valid bearer token for the given user claims.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(
        string sub = "auth0|test-user-001",
        string email = "testuser@example.com",
        string? nickname = null)
    {
        var token = TestJwtIssuer.GenerateToken(sub, email, nickname);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
