namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// What Bibliotaph handed back in the return address: the protected state from the path and
/// its <c>?bibliotaph=</c> outcome, passed on as is for the API to judge.
/// </summary>
public record BibliotaphResumeRequest(string State, string? Outcome);
