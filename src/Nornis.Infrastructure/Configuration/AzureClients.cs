using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;

namespace Nornis.Infrastructure.Configuration;

/// <summary>
/// The one place that decides how this process authenticates to Blob Storage and Service
/// Bus. Both hosts, every health probe and every processor build their clients here, so the
/// rule cannot drift between the API's copy and the Worker's.
///
/// The rule, per resource: a connection string wins when one is set; otherwise an endpoint
/// means "authenticate as this process's identity" through <see cref="DefaultAzureCredential"/>
/// — the container app's managed identity in Azure, the developer's <c>az login</c> on a
/// workstation; otherwise the resource is not configured and the caller decides what that
/// means for its feature (the Library switches off, the Worker refuses to start).
///
/// Why both forms exist: the deployed apps carry no secrets for these resources at all — the
/// identity is the credential — but the isolated local stack (<c>scripts/start-local.ps1</c>)
/// runs a Service Bus emulator that only speaks connection strings, and the Blob emulator
/// path is the same. Null, empty and whitespace all mean "unset" here, for either key; an
/// empty string shipped in appsettings must not read as a configured endpoint.
/// </summary>
public static class AzureClients
{
    /// <summary>
    /// One credential per process. <see cref="DefaultAzureCredential"/> caches tokens and is
    /// safe to share; building one per client would re-probe the credential chain each time.
    /// </summary>
    private static readonly Lazy<TokenCredential> Credential = new(() => new DefaultAzureCredential());

    public static BlobServiceClient? TryCreateBlobServiceClient(string? connectionString, string? serviceUri)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new BlobServiceClient(connectionString);
        }

        if (!string.IsNullOrWhiteSpace(serviceUri))
        {
            return new BlobServiceClient(new Uri(serviceUri), Credential.Value);
        }

        return null;
    }

    public static ServiceBusClient? TryCreateServiceBusClient(string? connectionString, string? fullyQualifiedNamespace)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new ServiceBusClient(connectionString);
        }

        if (!string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            return new ServiceBusClient(fullyQualifiedNamespace, Credential.Value);
        }

        return null;
    }

    /// <summary>
    /// The sentence a startup guard or a lazy "not configured" factory should say, so every
    /// host names the same two keys in the same order.
    /// </summary>
    public static string NotConfiguredHint(string connectionStringKey, string endpointKey) =>
        $"Set '{connectionStringKey}' (a connection string) or '{endpointKey}' (an endpoint, authenticated as this process's identity).";
}
