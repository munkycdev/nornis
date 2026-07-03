using Nornis.Api.Extensions;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Filters;

public class CampaignMemberFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.GetNornisUser();
        var campaignIdStr = httpContext.GetRouteValue("campaignId")?.ToString();

        if (!Guid.TryParse(campaignIdStr, out var campaignId))
        {
            return Results.NotFound();
        }

        var memberRepo = httpContext.RequestServices
            .GetRequiredService<ICampaignMemberRepository>();

        var member = await memberRepo.GetByCampaignAndUserAsync(
            campaignId, user.Id, httpContext.RequestAborted);

        if (member is null)
        {
            // Return 403 regardless of campaign existence for non-members (Req 8.5)
            return Results.Forbid();
        }

        httpContext.Items["CampaignMember"] = member;
        return await next(context);
    }
}
