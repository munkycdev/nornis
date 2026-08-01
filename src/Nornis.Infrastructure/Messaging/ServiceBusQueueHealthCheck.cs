using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nornis.Infrastructure.Messaging;

/// <summary>
/// Reports whether the extraction queue can be reached and written to.
///
/// Deliberately not the AspNetCore.HealthChecks.AzureServiceBus queue check: that one
/// reads queue runtime properties through <c>ServiceBusAdministrationClient</c>, which
/// needs Manage rights on the namespace. The API holds a send-only key, correctly — so
/// that check could only ever pass by widening production privileges to make a status
/// light go green.
///
/// Opening a batch instead asks the question the application actually cares about, using
/// only the rights it already has: it forces the AMQP send link open, which fails if the
/// namespace is unreachable, the queue is missing, or the credential cannot send. No
/// message is ever dispatched.
/// </summary>
public class ServiceBusQueueHealthCheck : IHealthCheck
{
    private readonly Lazy<ServiceBusSender> _sender;

    public ServiceBusQueueHealthCheck(ServiceBusClient client)
    {
        // One sender for the check's lifetime, matching ServiceBusExtractionQueueClient —
        // a link opened and torn down per scrape would be its own small load.
        _sender = new Lazy<ServiceBusSender>(
            () => client.CreateSender(ServiceBusExtractionQueueClient.QueueName));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var batch = await _sender.Value.CreateMessageBatchAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Queue '{ServiceBusExtractionQueueClient.QueueName}' is reachable.");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            // The failure that silently strands every source: the app keeps accepting
            // uploads and nothing ever extracts them.
            return HealthCheckResult.Unhealthy($"Queue '{ServiceBusExtractionQueueClient.QueueName}' does not exist.");
        }
        catch (UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("Not authorized to send to the extraction queue.");
        }
        catch (Exception ex)
        {
            // Message, not the exception object: /status renders check names and verdicts
            // to anonymous callers, and Service Bus exception text carries the namespace.
            return HealthCheckResult.Unhealthy($"Extraction queue unreachable ({ex.GetType().Name}).");
        }
    }
}
