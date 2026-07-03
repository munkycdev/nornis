using System.Security.Claims;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Middleware;

/// <summary>
/// Development-only middleware that bypasses Auth0 JWT validation by creating
/// a fake authenticated ClaimsPrincipal and provisioning a dev user.
/// This allows manual testing of all endpoints without Auth0 configuration.
///
/// NEVER enable this in production.
/// </summary>
public class DevAuthBypassMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DevAuthBypassMiddleware> _logger;

    private const string DevSubjectId = "dev|local-testing-user";
    private const string DevEmail = "dev@nornis.local";
    private const string DevUsername = "DevUser";

    public DevAuthBypassMiddleware(RequestDelegate next, ILogger<DevAuthBypassMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
    {
        // Skip for health endpoint
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // Set up a fake authenticated identity
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, DevSubjectId),
            new Claim("sub", DevSubjectId),
            new Claim(ClaimTypes.Email, DevEmail),
            new Claim("email", DevEmail),
            new Claim("nickname", DevUsername)
        };

        var identity = new ClaimsIdentity(claims, "DevBypass");
        context.User = new ClaimsPrincipal(identity);

        // Provision or retrieve the dev user
        var user = await userRepository.GetByAuth0SubjectIdAsync(DevSubjectId, context.RequestAborted);

        if (user is null)
        {
            user = await userRepository.CreateAsync(new User
            {
                Id = Guid.NewGuid(),
                Auth0SubjectId = DevSubjectId,
                Username = DevUsername,
                Email = DevEmail,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, context.RequestAborted);

            _logger.LogInformation("Dev auth bypass: created dev user {UserId}", user.Id);
        }

        context.Items["NornisUser"] = user;

        await _next(context);
    }
}
