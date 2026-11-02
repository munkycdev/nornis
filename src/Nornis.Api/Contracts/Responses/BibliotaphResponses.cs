namespace Nornis.Api.Contracts.Responses;

/// <summary>Profile › Bibliotaph. <c>Status</c> is none, connected or needs_reconnect.</summary>
public record BibliotaphConnectionResponse(
    bool Configured, string Status, DateTimeOffset? ConnectedAt, DateTimeOffset? LastUsedAt);

/// <summary>An address to send the browser to.</summary>
public record BibliotaphRedirectResponse(string Url);
