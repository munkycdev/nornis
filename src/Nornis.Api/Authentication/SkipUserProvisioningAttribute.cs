namespace Nornis.Api.Authentication;

/// <summary>
/// Marks an action that runs without a resolved Nornis user: <see cref="Middleware.UserProvisioningMiddleware"/>
/// neither looks one up nor creates one for the token's subject. The token is still validated and
/// authorized as usual. Only for an action that decides itself what the subject belongs to, which
/// today is claiming a sign-in link (feature 33).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SkipUserProvisioningAttribute : Attribute
{
}
