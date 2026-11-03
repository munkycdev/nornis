using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Prep.Covers;
using Nornis.Application.Services;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence.Repositories;

namespace Nornis.Infrastructure.Ai;

/// <summary>Who pays for an AI call, and the clients that make it (feature 39); shared by the API
/// and the Worker. Nornis's own <see cref="NornisChatClients"/> are registered by each host.</summary>
public static class AiFundingServiceCollectionExtensions
{
    /// <param name="maxRetries">The host's SDK retry count for clients built with users' keys,
    /// the same as for Nornis's own (<see cref="AzureOpenAiClientFactory"/>).</param>
    public static IServiceCollection AddAiFunding(this IServiceCollection services, IConfiguration configuration, int maxRetries)
    {
        var section = configuration.GetSection(AiModelsOptions.SectionName);
        var options = section.Get<AiModelsOptions>() ?? new AiModelsOptions();

        services.AddOptions<AiModelsOptions>()
            .Bind(section)
            .Validate(o => o.StartupProblem() is null, options.StartupProblem() ?? "AiModels options are invalid.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAiKeyProtector>(_ => AiKeyProtector.Create(configuration["AiKeys:KeyBlobUri"]));
        services.AddSingleton(new UserKeyChatClients(maxRetries));
        services.AddScoped<IAiKeyRepository, AiKeyRepository>();
        services.AddScoped<IAiFundingService, AiFundingService>();
        services.AddScoped<AiCallBinder>();
        services.AddScoped<IAiCallBinder>(sp => sp.GetRequiredService<AiCallBinder>());
        services.AddScoped<IAiImageBinder>(sp => sp.GetRequiredService<AiCallBinder>());
        services.AddScoped<ICoverArtGenerator, OpenAiCoverArtGenerator>();
        services.AddHttpClient<IAiKeyChecker, AiKeyChecker>()
            .ConfigurePrimaryHttpMessageHandler(PublicEndpointGuard.CreateHandler);

        return services;
    }
}
