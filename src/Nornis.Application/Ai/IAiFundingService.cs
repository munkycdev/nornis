namespace Nornis.Application.Ai;

/// <summary>
/// The one place that decides who pays for an AI call and on which model (feature 39). Called at
/// the moment of the call by the infrastructure binder, so a background job decides with the keys
/// that exist when it runs, not when it was queued.
/// </summary>
public interface IAiFundingService
{
    /// <param name="nornisModel">The model Nornis's own key uses for this work: the configured
    /// deployment the calling service asked for. It is what runs whenever Nornis pays.</param>
    Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct);
}
