using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nornis.Web.Authentication;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Where the Web app keeps the keys that sign its cookies. Kept in the container, they were
/// regenerated on every deploy, which signed everyone out and broke any form open at the time.
/// </summary>
[TestFixture]
public class DataProtectionKeysTests
{
    private static KeyManagementOptions Configure(string? keyBlobUri, bool signInEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DataProtectionKeys.KeyBlobUriKey] = keyBlobUri })
            .Build();
        var services = new ServiceCollection();

        services.AddNornisDataProtection(configuration, signInEnabled);

        return services.BuildServiceProvider().GetRequiredService<IOptions<KeyManagementOptions>>().Value;
    }

    [Test]
    public void AConfiguredBlob_HoldsTheKeyRing()
    {
        var options = Configure("https://example.blob.core.windows.net/dataprotection/keys.xml", signInEnabled: true);

        Assert.That(options.XmlRepository, Is.Not.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void WithoutSignIn_NoBlobLeavesTheKeysLocal(string? keyBlobUri)
    {
        // Local dev runs anonymous; a key ring that dies with the process costs nothing there.
        var options = Configure(keyBlobUri, signInEnabled: false);

        Assert.That(options.XmlRepository, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    public void WithSignIn_NoBlobRefusesToStart(string? keyBlobUri)
    {
        // A deployment that signs people in and forgot the key store would go back to signing
        // everyone out on each deploy, with nothing failing. It fails here instead.
        Assert.That(() => Configure(keyBlobUri, signInEnabled: true), Throws.InvalidOperationException);
    }
}
