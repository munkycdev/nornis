using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Middleware;

public class UserProvisioningMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserProvisioningMiddleware> _logger;
    private readonly string _claimsNamespace;

    public UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        // Access tokens carry profile data only as namespaced custom claims, added by
        // the tenant's post-login Action. The namespace is shared with Chronicis.
        _claimsNamespace = (configuration["Auth0:ClaimsNamespace"] ?? "https://chronicis.app").TrimEnd('/');
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? context.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(sub))
        {
            context.Response.StatusCode = 401;
            return;
        }

        var email = context.User.FindFirstValue(ClaimTypes.Email)
                    ?? context.User.FindFirstValue("email")
                    ?? context.User.FindFirstValue($"{_claimsNamespace}/email");

        if (string.IsNullOrEmpty(email))
        {
            context.Response.StatusCode = 401;
            return;
        }

        try
        {
            var user = await userRepository.GetByAuth0SubjectIdAsync(sub, context.RequestAborted);

            if (user is null)
            {
                var nickname = context.User.FindFirstValue("nickname")
                               ?? context.User.FindFirstValue($"{_claimsNamespace}/name")
                               ?? sub;
                user = await userRepository.CreateAsync(new User
                {
                    Id = Guid.NewGuid(),
                    Auth0SubjectId = sub,
                    Username = nickname,
                    Email = email,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, context.RequestAborted);
            }

            context.Items["NornisUser"] = user;
        }
        catch (DbUpdateException)
        {
            var user = await userRepository.GetByAuth0SubjectIdAsync(sub, context.RequestAborted);
            if (user is not null)
            {
                context.Items["NornisUser"] = user;
            }
            else
            {
                _logger.LogError("User provisioning failed for sub {Sub}", sub);
                context.Response.StatusCode = 503;
                return;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User provisioning infrastructure error for sub {Sub}", sub);
            context.Response.StatusCode = 503;
            return;
        }

        await _next(context);
    }
}
