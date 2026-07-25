namespace Nornis.Web.State;

/// <summary>
/// Per-circuit flag for the GM's "view as player" mode. Lives outside <see cref="WorldState"/>
/// so <see cref="ApiClient.NornisApiClient"/> can read it without a circular dependency:
/// while set, the client stamps every API request with the view-as header and the server
/// resolves the membership as Player for that request. Deliberately ephemeral — circuit
/// state only, never persisted, so a reload always returns to the real role.
/// </summary>
public class ViewAsState
{
    public bool ViewingAsPlayer { get; set; }
}
