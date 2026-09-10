using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// How a member is named on screen when they have not chosen a display name: the same
/// generated label the API uses (<c>MemberDisplayName</c> on the server), so a member reads
/// the same everywhere. One place, because the Members page, the Party page and the settings
/// panel all show members and used to each carry a copy.
/// </summary>
public static class MemberDisplay
{
    public static string Name(WorldMember m) =>
        !string.IsNullOrWhiteSpace(m.DisplayName) ? m.DisplayName! : $"User {m.UserId.ToString()[..8]}";

    public static string Initial(WorldMember m) => Name(m)[..1].ToUpperInvariant();

    /// <summary>Observers are "Fly on the wall" in the UI; the internal role name stays Observer.</summary>
    public static string RoleLabel(string role) => role == "Observer" ? "Fly on the wall" : role;
}
