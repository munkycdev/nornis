using Nornis.Domain.Entities;

namespace Nornis.Api.Extensions;

public static class HttpContextExtensions
{
    public static User GetNornisUser(this HttpContext context)
    {
        return context.Items["NornisUser"] as User
            ?? throw new InvalidOperationException("NornisUser not found in HttpContext. Ensure UserProvisioningMiddleware is in the pipeline.");
    }

    public static CampaignMember GetCampaignMember(this HttpContext context)
    {
        return context.Items["CampaignMember"] as CampaignMember
            ?? throw new InvalidOperationException("CampaignMember not found in HttpContext. Ensure CampaignMemberFilter is applied.");
    }
}
