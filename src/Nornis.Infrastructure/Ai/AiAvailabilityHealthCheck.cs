using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Reports one AI provider's availability from observed traffic rather than a probe — see
/// <see cref="IAiOutcomeMonitor"/> for why a probe would be the wrong instrument. Registered once
/// per provider (feature 39): <c>azure-openai</c> for Nornis's own resource, which serves the
/// trial and embeddings, and one <c>ai-…</c> entry per provider users' keys reach.
///
/// Idle reads Healthy on purpose. Most of the time nobody is asking the Loremaster
/// anything, and a status page that goes amber overnight teaches people to ignore it.
/// </summary>
public class AiAvailabilityHealthCheck : IHealthCheck
{
    /// <summary>
    /// Long enough to still be judging the provider rather than one unlucky minute,
    /// short enough that recovery shows up without waiting for the next scrape hour.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IAiOutcomeMonitor _monitor;
    private readonly IAiPauseGate _pauseGate;
    private readonly AiProvider _provider;

    public AiAvailabilityHealthCheck(IAiOutcomeMonitor monitor, IAiPauseGate pauseGate, AiProvider provider)
    {
        _monitor = monitor;
        _pauseGate = pauseGate;
        _provider = provider;
    }

    /// <summary>Nornis's own resource, then each provider a user's key can reach today.</summary>
    public static IReadOnlyList<AiProvider> ReportedProviders =>
        [AiProvider.AzureOpenAi, .. AiModelsOptions.CatalogProviders.Order(), AiProvider.OpenAiCompatible];

    /// <summary>The <c>/status</c> entry each provider reports under.</summary>
    public static string EntryName(AiProvider provider) => provider switch
    {
        AiProvider.AzureOpenAi => "azure-openai",
        AiProvider.OpenAi => "ai-openai",
        AiProvider.Anthropic => "ai-anthropic",
        AiProvider.Gemini => "ai-gemini",
        AiProvider.OpenAiCompatible => "ai-openai-compatible",
        _ => "ai-" + provider.ToString().ToLowerInvariant(),
    };

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // A deliberate pause must not read as a provider outage. It is reported Degraded
        // rather than Healthy because the feature genuinely is unavailable — but the text
        // says who turned it off, which is the difference between "we did this" and "Azure
        // is broken" on a page someone reads at 2am.
        if (await _pauseGate.GetAsync(cancellationToken) is { IsPaused: true } paused)
        {
            return HealthCheckResult.Degraded(string.IsNullOrWhiteSpace(paused.Reason)
                ? "AI is paused by an operator."
                : $"AI is paused by an operator: {paused.Reason}");
        }

        var snapshot = _monitor.Snapshot(_provider, Window, DateTimeOffset.UtcNow);

        if (snapshot.Total == 0)
        {
            return HealthCheckResult.Healthy("No AI calls in the last 15 minutes.");
        }

        // Every observed call failing is the signal. A mixed result means the provider is
        // answering and something about particular requests is at fault — a content filter
        // rejection, a budget refusal — which is not an outage and must not read as one.
        return snapshot.Failures == snapshot.Total
            ? HealthCheckResult.Degraded($"The last {snapshot.Total} AI call(s) all failed.")
            : HealthCheckResult.Healthy($"{snapshot.Total - snapshot.Failures} of {snapshot.Total} recent AI call(s) succeeded.");
    }
}
