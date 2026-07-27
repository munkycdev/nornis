namespace Nornis.Api.Contracts.Requests;

/// <summary>What a browser reports about itself when it subscribes. The keys are opaque to us
/// — they exist so the push service can encrypt the payload for that browser alone.</summary>
public record SavePushSubscriptionRequest(
    string Endpoint,
    string P256dh,
    string Auth,
    string? Label = null);
