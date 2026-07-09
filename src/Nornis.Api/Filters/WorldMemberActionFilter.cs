using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Filters;

/// <summary>
/// MVC action filter that resolves WorldMember from route {worldId} parameter
/// and stores it in HttpContext.Items for downstream controller access.
/// Apply via [ServiceFilter(typeof(WorldMemberActionFilter))] on world-scoped actions/controllers.
/// </summary>
public class WorldMemberActionFilter : IAsyncActionFilter
{
    private readonly IWorldMemberRepository _memberRepository;

    public WorldMemberActionFilter(IWorldMemberRepository memberRepository)
    {
        _memberRepository = memberRepository;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.GetNornisUser();
        var worldIdStr = httpContext.GetRouteValue("worldId")?.ToString();

        if (!Guid.TryParse(worldIdStr, out var worldId))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var member = await _memberRepository.GetByWorldAndUserAsync(
            worldId, user.Id, httpContext.RequestAborted);

        if (member is null)
        {
            // Return 403 regardless of world existence for non-members (Req 8.5)
            context.Result = new ObjectResult(new ErrorResponse("access_denied", "You are not a member of this world."))
            {
                StatusCode = 403
            };
            return;
        }

        httpContext.Items["WorldMember"] = member;
        await next();
    }
}
