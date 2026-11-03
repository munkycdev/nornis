namespace Nornis.Api.Contracts.Requests;

/// <summary>The link code the sign-in round trip carried back (feature 33).</summary>
public record ClaimSignInRequest(string Code);
