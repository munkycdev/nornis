using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Infrastructure.Messaging;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Messaging;

/// <summary>
/// The check exists because the packaged Azure Service Bus check needs Manage rights and
/// the API deliberately holds a send-only key. What is worth pinning here is the part that
/// choice implies: it must fail closed, and it must never put namespace detail into a
/// response that goes out anonymously.
/// </summary>
[TestFixture]
public class ServiceBusQueueHealthCheckTests
{
    /// <summary>
    /// A client pointed at a namespace that cannot resolve. Nothing is sent either way —
    /// the check only ever opens a link — so this exercises the real failure path.
    /// </summary>
    private static ServiceBusClient UnreachableClient() =>
        new(
            "Endpoint=sb://nornis-status-check-does-not-exist.servicebus.windows.net/;"
                + "SharedAccessKeyName=probe;SharedAccessKey=cHJvYmU=",
            new ServiceBusClientOptions
            {
                RetryOptions = new ServiceBusRetryOptions
                {
                    MaxRetries = 0,
                    TryTimeout = TimeSpan.FromSeconds(2)
                }
            });

    [Test]
    public async Task UnreachableNamespace_IsUnhealthy()
    {
        await using var client = UnreachableClient();

        var result = await new ServiceBusQueueHealthCheck(client)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task AFailure_DoesNotNameTheNamespace()
    {
        await using var client = UnreachableClient();

        var result = await new ServiceBusQueueHealthCheck(client)
            .CheckHealthAsync(new HealthCheckContext());

        // /status is anonymous. Service Bus exception text carries the fully-qualified
        // namespace, so the description must be built from the exception type, not from it.
        Assert.Multiple(() =>
        {
            Assert.That(result.Description, Does.Not.Contain("servicebus.windows.net"));
            Assert.That(result.Description, Does.Not.Contain("nornis-status-check-does-not-exist"));
        });
    }

    [Test]
    public async Task AFailure_DoesNotThrow()
    {
        await using var client = UnreachableClient();

        // A throwing check would surface as an unhandled 500 from /status rather than a
        // red row, losing every other check's verdict along with it.
        var result = await new ServiceBusQueueHealthCheck(client)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Description, Is.Not.Null);
    }
}
