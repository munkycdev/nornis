using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence.Repositories;

namespace Nornis.Infrastructure.Bibliotaph;

/// <summary>The Bibliotaph integration's registrations, shared by the API and the Worker.</summary>
public static class BibliotaphServiceCollectionExtensions
{
    public static IServiceCollection AddBibliotaph(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(BibliotaphOptions.SectionName);
        var options = section.Get<BibliotaphOptions>() ?? new BibliotaphOptions();

        services.AddOptions<BibliotaphOptions>()
            .Bind(section)
            .Validate(o => o.StartupProblem() is null, options.StartupProblem() ?? "Bibliotaph options are invalid.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IBibliotaphConnectionRepository, BibliotaphConnectionRepository>();
        services.AddScoped<IBibliotaphConnectionResolver, BibliotaphConnectionResolver>();
        services.AddScoped<IBibliotaphLibrarySync, BibliotaphLibrarySync>();
        services.AddSingleton<BibliotaphReconcileThrottle>();
        services.AddSingleton<IBibliotaphTokenProtector>(_ => BibliotaphTokenProtector.Create(options.KeyBlobUri));
        services.AddSingleton<IBibliotaphTokenSource, Auth0BibliotaphTokenSource>();

        services.AddHttpClient(Auth0BibliotaphTokenSource.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        // Each call carries its own, shorter budget; this is only the backstop.
        services.AddHttpClient<IBibliotaphClient, BibliotaphClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
