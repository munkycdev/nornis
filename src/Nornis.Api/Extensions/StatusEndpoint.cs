using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nornis.Api.Extensions;

/// <summary>
/// The response shape for GET /status, and the one rule that governs it: the payload is
/// public, so it carries names and verdicts and nothing else.
/// </summary>
public static class StatusEndpoint
{
    public const string CorsPolicyName = "status-dashboard";

    /// <summary>Tag marking a check as a dependency probe, so it lands on /status and never on /health.</summary>
    public const string DependencyTag = "deps";

    /// <summary>Tag marking a check as "is this deploy broken", which is all /health means.</summary>
    public const string LivenessTag = "live";

    /// <summary>
    /// Writes the aggregate plus one row per check.
    ///
    /// Deliberately not written: check descriptions and exception text. Those are where the
    /// useful detail lives and also where connection strings, hostnames and schema names
    /// leak — and this endpoint is anonymous by design. Diagnosis happens in App Insights,
    /// which has the full report; the page only needs to say which row is red.
    /// </summary>
    public static Task WriteStatusResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = (int)entry.Value.Duration.TotalMilliseconds
                })
                .OrderBy(check => check.name)
                .ToArray()
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
