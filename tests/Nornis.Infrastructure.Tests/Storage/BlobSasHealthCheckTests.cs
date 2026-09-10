using Azure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Application.Storage;
using Nornis.Infrastructure.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Storage;

/// <summary>
/// The check exists because a container-scoped role passed the packaged blob check while
/// every SAS-minting endpoint failed. What is worth pinning: it asks the same service the
/// endpoints use, it names the missing right when the answer is 403, and it never puts the
/// minted URL or the account into a response that goes out anonymously.
/// </summary>
[TestFixture]
public class BlobSasHealthCheckTests
{
    private const string AccountUrl = "https://stnornisprobe.blob.core.windows.net/nornis-library/status/probe?sv=2026&sig=secret";

    [Test]
    public async Task ASignedUrl_IsHealthy()
    {
        var storage = Substitute.For<IBlobStorageService>();
        storage.GenerateDownloadSasUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(AccountUrl);

        var result = await new BlobSasHealthCheck(storage).CheckHealthAsync(new HealthCheckContext());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Description, Does.Not.Contain("sig="), "the SAS is a credential and stays out of the verdict");
            Assert.That(result.Description, Does.Not.Contain("stnornisprobe"));
        });
    }

    [Test]
    public async Task AForbiddenKey_NamesTheMissingRole()
    {
        // The production failure of 2026-09-10, verbatim in shape: the account answers 403
        // AuthorizationPermissionMismatch to GetUserDelegationKey.
        var storage = Substitute.For<IBlobStorageService>();
        storage.GenerateDownloadSasUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(403, "This request is not authorized to perform this operation using this permission. stnornisprobe"));

        var result = await new BlobSasHealthCheck(storage).CheckHealthAsync(new HealthCheckContext());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(result.Description, Does.Contain("Storage Blob Delegator"));
            Assert.That(result.Description, Does.Not.Contain("stnornisprobe"));
        });
    }

    [Test]
    public async Task AnyOtherFailure_IsUnhealthyAndQuiet()
    {
        var storage = Substitute.For<IBlobStorageService>();
        storage.GenerateDownloadSasUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(503, "Server busy at stnornisprobe.blob.core.windows.net"));

        var result = await new BlobSasHealthCheck(storage).CheckHealthAsync(new HealthCheckContext());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(result.Description, Does.Contain(nameof(RequestFailedException)));
            Assert.That(result.Description, Does.Not.Contain("stnornisprobe"));
        });
    }
}
