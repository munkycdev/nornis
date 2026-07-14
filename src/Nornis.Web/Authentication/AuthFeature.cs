namespace Nornis.Web.Authentication;

/// <summary>
/// Whether Auth0 login is configured for this deployment. Locally (no Auth0 section)
/// the app runs anonymous against the API's dev bypass, and the UI hides account
/// controls; the router also skips authorization enforcement.
/// </summary>
public record AuthFeature(bool Enabled);
