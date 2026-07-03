using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Domain.Repositories;

namespace Nornis.Api.Filters;

/// <summary>
/// MVC action filter that resolves CampaignMember from route {campaignId} parameter
/// and stores it in HttpContext.Items for downstream controller access.
/// Apply via [ServiceFilter(typeof(CampaignMemberActionFilter))] on campaign-scoped actions/controllers.
/// </summary>
public class CampaignMemberActionFilter : IAsyncActionFilter
{
    private readonly ICampaignMemberRepository _memberRepository;

    public CampaignMemberActionFilter(ICampaignMemberRepository memberRepository)
    {
        _memberRepository = memberRepository;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.GetNornisUser();
        var campaignIdStr = httpContext.GetRouteValue("campaignId")?.ToString();

        if (!Guid.TryParse(campaignIdStr, out var campaignId))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var member = await _memberRepository.GetByCampaignAndUserAsync(
            campaignId, user.Id, httpContext.RequestAborted);

        if (member is null)
        {
            // Return 403 regardless of campaign existence for non-members (Req 8.5)
            context.Result = new ObjectResult(new ErrorResponse("access_denied", "You are not a member of this campaign."))
            {
                StatusCode = 403
            };
            return;
        }

        httpContext.Items["CampaignMember"] = member;
        await next();
    }
}
