using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// The one place that decides who pays for an AI call and on which model (feature 39). Called at
/// the moment of the call by the infrastructure binder, so a background job decides with the keys
/// that exist when it runs, not when it was queued.
/// </summary>
public interface IAiFundingService
{
    /// <param name="nornisModel">The model Nornis's own key uses for this work: the configured
    /// deployment the calling service asked for. It is what runs whenever Nornis pays. Null when
    /// Nornis has no deployment for the work (cover art, feature 38): then only a key can pay, and
    /// neither Nornis nor the trial (which runs on Nornis's deployments) is a fallback.</param>
    /// <param name="needsImages">The call sends images, so only a model that reads them will do.</param>
    /// <exception cref="AiRefusalException">Nothing can pay, or the key that would pay was rejected.</exception>
    Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string? nornisModel, bool needsImages, CancellationToken ct);
}
