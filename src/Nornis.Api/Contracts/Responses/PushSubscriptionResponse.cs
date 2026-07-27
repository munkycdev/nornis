namespace Nornis.Api.Contracts.Responses;

/// <param name="Configured">False when the server has no VAPID keys, so the UI can explain
/// rather than offer a button that cannot work.</param>
public record PushConfigResponse(bool Configured, string? PublicKey);

/// <summary>One browser a user has enabled notifications on. The endpoint and keys are
/// deliberately not returned — they are credentials for talking to that browser.</summary>
public record PushSubscriptionResponse(
    Guid Id,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSucceededAt);
