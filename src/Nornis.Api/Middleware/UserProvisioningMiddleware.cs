using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Middleware;

public class UserProvisioningMiddleware
{
    /// <summary>
    /// How long a resolved subject-to-user mapping is reused. The mapping itself is immutable —
    /// an Auth0 subject id keeps the same Nornis user forever, and there is no delete path — so
    /// this could be far longer. Ten minutes bounds how long a profile edit takes to appear and
    /// keeps a restart-free way to recover from anything unexpected.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Cached as an immutable snapshot, never as the entity.
    ///
    /// A cached <see cref="User"/> would be one object shared by every concurrent request for
    /// that subject; anything that mutated it — today nothing does, tomorrow is another matter —
    /// would corrupt other requests in flight. A record costs one small allocation per request
    /// and makes that impossible.
    /// </summary>
    private sealed record CachedUser(Guid Id, string Auth0SubjectId, string Username, string Email);

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

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository, IMemoryCache cache)
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

        var cacheKey = $"user:sub:{sub}";

        // This middleware runs on every authenticated request, so this lookup was the single
        // most-executed query in the system — and it exists only to turn a JWT subject into a
        // Guid. Downstream code reads nothing but Id off the result.
        //
        // CRITICAL: `_next` is invoked exactly once, at the very end, outside the try/catch below.
        //
        // Resolving the user is the only thing those catch clauses are meant to cover. Calling
        // `_next` inside them would put the entire application inside this middleware's error
        // handling: a controller's DbUpdateException would be caught here and the request
        // re-executed from routing — running every side effect twice — and any downstream 500
        // would surface as a 503 logged as a user-provisioning failure, sending real controller
        // bugs to the wrong place. Keep the resolution and the continuation separate.
        User? resolved;

        if (cache.TryGetValue<CachedUser>(cacheKey, out var cached) && cached is not null)
        {
            resolved = ToUser(cached);
        }
        else
        {
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

                cache.Set(cacheKey, Snapshot(user), CacheTtl);
                resolved = user;
            }
            catch (DbUpdateException)
            {
                // Lost a create race with a concurrent request for the same subject; the unique
                // index on Auth0SubjectId means the winner's row is now readable. Deliberately
                // not cached — this path is rare and the next request can populate normally.
                var user = await userRepository.GetByAuth0SubjectIdAsync(sub, context.RequestAborted);
                if (user is null)
                {
                    _logger.LogError("User provisioning failed for sub {Sub}", sub);
                    context.Response.StatusCode = 503;
                    return;
                }

                resolved = user;
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
        }

        context.Items["NornisUser"] = resolved;

        await _next(context);
    }

    private static CachedUser Snapshot(User user) =>
        new(user.Id, user.Auth0SubjectId, user.Username, user.Email);

    /// <summary>
    /// Rebuilds the entity downstream code expects. Only <c>Id</c> is read anywhere in the API,
    /// but the timestamps are deliberately left default rather than invented: a caller that
    /// starts depending on <c>CreatedAt</c> should fail obviously rather than silently read a
    /// fabricated value.
    /// </summary>
    private static User ToUser(CachedUser cached) => new()
    {
        Id = cached.Id,
        Auth0SubjectId = cached.Auth0SubjectId,
        Username = cached.Username,
        Email = cached.Email,
    };
}
