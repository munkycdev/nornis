using Nornis.Api.Extensions;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Filters;

public class WorldMemberFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.GetNornisUser();
        var worldIdStr = httpContext.GetRouteValue("worldId")?.ToString();

        if (!Guid.TryParse(worldIdStr, out var worldId))
        {
            return Results.NotFound();
        }

        var memberRepo = httpContext.RequestServices
            .GetRequiredService<IWorldMemberRepository>();

        var member = await memberRepo.GetByWorldAndUserAsync(
            worldId, user.Id, httpContext.RequestAborted);

        if (member is null)
        {
            // Return 403 regardless of world existence for non-members (Req 8.5)
            return Results.Forbid();
        }

        httpContext.Items["WorldMember"] = member;
        return await next(context);
    }
}
