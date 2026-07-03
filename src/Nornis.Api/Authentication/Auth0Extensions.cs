using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Nornis.Api.Authentication;

public static class Auth0Extensions
{
    public static IServiceCollection AddAuth0Authentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var domain = configuration["Auth0:Domain"]
            ?? throw new InvalidOperationException("Auth0:Domain configuration is required.");
        var audience = configuration["Auth0:Audience"]
            ?? throw new InvalidOperationException("Auth0:Audience configuration is required.");

        var authority = $"https://{domain}/";

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = options.DefaultPolicy;
        });

        return services;
    }
}
