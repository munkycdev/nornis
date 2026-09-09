using Nornis.Infrastructure.Configuration;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Configuration;

/// <summary>
/// The one rule for how the process authenticates to Blob and Service Bus, pinned: a
/// connection string wins, an endpoint alone means "this process's identity", nothing means
/// not configured. Nothing here connects — construction is local in both SDKs — so what is
/// asserted is which door the rule chose, read back from the client it built.
/// </summary>
[TestFixture]
public class AzureClientsTests
{
    private const string BlobConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=fromconnstring;AccountKey=dGVzdC1rZXk=;EndpointSuffix=core.windows.net";

    private const string ServiceBusConnectionString =
        "Endpoint=sb://fromconnstring.servicebus.windows.net/;SharedAccessKeyName=send;SharedAccessKey=dGVzdC1rZXk=";

    [Test]
    public void Blob_ConnectionStringWins_EvenWhenAnEndpointIsAlsoSet()
    {
        var client = AzureClients.TryCreateBlobServiceClient(BlobConnectionString, "https://fromendpoint.blob.core.windows.net/");

        Assert.That(client!.AccountName, Is.EqualTo("fromconnstring"));
        Assert.That(client.CanGenerateAccountSasUri, Is.True, "a shared-key client can sign SAS itself");
    }

    [Test]
    public void Blob_EndpointAlone_AuthenticatesAsTheProcessIdentity()
    {
        var client = AzureClients.TryCreateBlobServiceClient(null, "https://fromendpoint.blob.core.windows.net/");

        Assert.That(client!.AccountName, Is.EqualTo("fromendpoint"));
        Assert.That(client.CanGenerateAccountSasUri, Is.False, "an identity client has no key to sign with");
    }

    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase("   ", "   ")]
    public void Blob_NothingSet_IsNotConfigured(string? connectionString, string? serviceUri)
    {
        Assert.That(AzureClients.TryCreateBlobServiceClient(connectionString, serviceUri), Is.Null);
    }

    [Test]
    public void ServiceBus_ConnectionStringWins_EvenWhenANamespaceIsAlsoSet()
    {
        var client = AzureClients.TryCreateServiceBusClient(ServiceBusConnectionString, "fromnamespace.servicebus.windows.net");

        Assert.That(client!.FullyQualifiedNamespace, Is.EqualTo("fromconnstring.servicebus.windows.net"));
    }

    [Test]
    public void ServiceBus_NamespaceAlone_AuthenticatesAsTheProcessIdentity()
    {
        var client = AzureClients.TryCreateServiceBusClient("", "fromnamespace.servicebus.windows.net");

        Assert.That(client!.FullyQualifiedNamespace, Is.EqualTo("fromnamespace.servicebus.windows.net"));
    }

    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase("   ", "   ")]
    public void ServiceBus_NothingSet_IsNotConfigured(string? connectionString, string? fullyQualifiedNamespace)
    {
        Assert.That(AzureClients.TryCreateServiceBusClient(connectionString, fullyQualifiedNamespace), Is.Null);
    }

    /// <summary>
    /// The hint every host prints names both doors in the same order, so a person reading a
    /// startup failure learns the identity path exists instead of going looking for a secret.
    /// </summary>
    [Test]
    public void NotConfiguredHint_NamesBothKeys()
    {
        var hint = AzureClients.NotConfiguredHint("ServiceBus:ConnectionString", "ServiceBus:FullyQualifiedNamespace");

        Assert.That(hint, Does.Contain("ServiceBus:ConnectionString").And.Contain("ServiceBus:FullyQualifiedNamespace"));
        Assert.That(hint, Does.Contain("identity"));
    }
}
