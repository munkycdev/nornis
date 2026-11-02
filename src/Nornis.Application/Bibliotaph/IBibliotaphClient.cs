namespace Nornis.Application.Bibliotaph;

/// <summary>
/// Bibliotaph's /api/nornis/v1, called with the connection of <c>connectionUserId</c> (a world's
/// creator). Every call has its own time budget from <see cref="Configuration.BibliotaphOptions"/>
/// and none retries: only the outbox retries. A refused grant marks the connection Needs
/// reconnect here, so no caller has to.
/// </summary>
public interface IBibliotaphClient
{
    Task<BibliotaphResult<BibliotaphAccepted>> SendWorldEventsAsync(
        Guid connectionUserId, IReadOnlyList<BibliotaphWorldEventMessage> events, CancellationToken ct);

    Task<BibliotaphResult<BibliotaphBookList>> ListBooksAsync(
        Guid connectionUserId, Guid worldId, CancellationToken ct);

    Task<BibliotaphResult<BibliotaphCover>> GetCoverAsync(
        Guid connectionUserId, string coverName, CancellationToken ct);

    Task<BibliotaphResult<BibliotaphPassages>> FindPassagesAsync(
        Guid connectionUserId, Guid worldId, BibliotaphPassagesRequest request, CancellationToken ct);

    /// <summary>Page text for 0-based, inclusive page indexes.</summary>
    Task<BibliotaphResult<BibliotaphPageTexts>> GetPagesAsync(
        Guid connectionUserId, Guid worldId, string documentId, int fromIndex, int toIndex, CancellationToken ct);

    Task<BibliotaphResult<BibliotaphTicket>> MintReadingTicketAsync(
        Guid connectionUserId, Guid worldId, BibliotaphTicketRequest request, CancellationToken ct);

    Task<BibliotaphResult<BibliotaphAccepted>> WarmAsync(Guid connectionUserId, CancellationToken ct);
}
