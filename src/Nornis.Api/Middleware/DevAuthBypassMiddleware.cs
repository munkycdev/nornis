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
    private readonly string _subjectId;
    private readonly string _email;
    private readonly string _username;

    public DevAuthBypassMiddleware(RequestDelegate next, ILogger<DevAuthBypassMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        // DevAuth:SubjectId lets a local run act as an existing user (e.g. against a
        // shared database whose data belongs to a real account). Defaults preserve the
        // classic local-only dev user.
        _subjectId = configuration["DevAuth:SubjectId"] ?? "dev|local-testing-user";
        _email = configuration["DevAuth:Email"] ?? "dev@nornis.local";
        _username = configuration["DevAuth:Username"] ?? "DevUser";
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
    {
        // Skip for the health endpoint and the public read-only API — the latter must
        // stay genuinely anonymous locally or the public path can't be exercised in dev.
        if (context.Request.Path.StartsWithSegments("/health")
            || context.Request.Path.StartsWithSegments("/api/public"))
        {
            await _next(context);
            return;
        }

        // Set up a fake authenticated identity
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _subjectId),
            new Claim("sub", _subjectId),
            new Claim(ClaimTypes.Email, _email),
            new Claim("email", _email),
            new Claim("nickname", _username)
        };

        var identity = new ClaimsIdentity(claims, "DevBypass");
        context.User = new ClaimsPrincipal(identity);

        // Provision or retrieve the dev user
        var user = await userRepository.GetByAuth0SubjectIdAsync(_subjectId, context.RequestAborted);

        if (user is null)
        {
            user = await userRepository.CreateAsync(new User
            {
                Id = Guid.NewGuid(),
                Auth0SubjectId = _subjectId,
                Username = _username,
                Email = _email,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, context.RequestAborted);

            _logger.LogInformation("Dev auth bypass: created dev user {UserId}", user.Id);
        }

        context.Items["NornisUser"] = user;

        await _next(context);
    }
}
