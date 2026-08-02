namespace Nornis.Web.State;

/// <summary>
/// The role names as the API spells them. Web owns its contracts (see Contracts.cs), so these
/// are string literals on the wire rather than the Domain's <c>WorldRole</c> enum — which is
/// exactly why they need one home: nothing here can catch a typo, and a mistyped role reads as
/// "not a GM" and silently hides the UI rather than failing.
/// </summary>
public static class WorldRoles
{
    public const string Gm = "GM";
    public const string Player = "Player";
    public const string Observer = "Observer";
}
