namespace Nornis.Api.Contracts.Requests;

/// <summary>An anonymous public ask. Single-shot by design: no conversation context is
/// accepted, keeping public spend bounded and predictable against the monthly cap.</summary>
public record PublicAskRequest(string Question);
