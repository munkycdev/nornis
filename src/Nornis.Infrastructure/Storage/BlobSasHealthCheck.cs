using Azure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Application.Storage;

namespace Nornis.Infrastructure.Storage;

/// <summary>
/// Reports whether the app can hand a browser a blob URL — mint a SAS — which under managed
/// identity means obtaining a user delegation key from the storage account.
///
/// The packaged blob check answers a different question: it lists the container, which the
/// container-scoped data role allows. Minting a delegation key is an account-level ask that
/// role does not cover, and on 2026-09-10 production sat green for two days with every
/// SAS-minting endpoint (map, library downloads, uploads) returning 500 — the health probe
/// had never asked for the one right that was missing. This check asks, through the same
/// service the endpoints use, so its cached key makes the probe free when things are well
/// and its failure is the endpoints' failure when they are not. No blob is touched: a SAS
/// is a signature, not a request.
/// </summary>
public class BlobSasHealthCheck : IHealthCheck
{
    /// <summary>A path that need not exist; signing does not look it up.</summary>
    private const string ProbePath = "status/probe";

    private readonly IBlobStorageService _storage;

    public BlobSasHealthCheck(IBlobStorageService storage)
    {
        _storage = storage;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // The URL itself is a bearer credential for the probe path; it goes nowhere.
            await _storage.GenerateDownloadSasUrlAsync(ProbePath, cancellationToken);
            return HealthCheckResult.Healthy("Blob URLs can be minted.");
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            return HealthCheckResult.Unhealthy(
                "Not authorized to mint a delegation key: the identity needs Storage Blob Delegator at the storage-account scope.");
        }
        catch (Exception ex)
        {
            // Message, not the exception object: /status renders to anonymous callers and
            // storage exception text carries the account name.
            return HealthCheckResult.Unhealthy($"Blob URLs cannot be minted ({ex.GetType().Name}).");
        }
    }
}
