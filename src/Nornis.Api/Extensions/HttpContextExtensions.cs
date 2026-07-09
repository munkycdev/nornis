using Nornis.Domain.Entities;

namespace Nornis.Api.Extensions;

public static class HttpContextExtensions
{
    public static User GetNornisUser(this HttpContext context)
    {
        return context.Items["NornisUser"] as User
            ?? throw new InvalidOperationException("NornisUser not found in HttpContext. Ensure UserProvisioningMiddleware is in the pipeline.");
    }

    public static WorldMember GetWorldMember(this HttpContext context)
    {
        return context.Items["WorldMember"] as WorldMember
            ?? throw new InvalidOperationException("WorldMember not found in HttpContext. Ensure WorldMemberFilter is applied.");
    }
}
