namespace Nornis.Api.Contracts.Requests;

/// <summary>Open a book. <c>StartPage</c> is 0-based, as Bibliotaph's reader counts.</summary>
public record LibraryReadRequest(int? StartPage);
