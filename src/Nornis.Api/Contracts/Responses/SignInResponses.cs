namespace Nornis.Api.Contracts.Responses;

/// <summary>One sign-in on Profile (feature 33). <c>Current</c> is the one this request's token uses.</summary>
public record SignInResponse(Guid Id, string Provider, DateTimeOffset AddedAt, bool Current);

/// <summary>A link code to carry through the sign-in round trip.</summary>
public record SignInLinkResponse(string Code);

/// <summary>How a claimed link ended: <c>linked</c> or <c>already-linked</c>.</summary>
public record SignInClaimResponse(string Outcome);
