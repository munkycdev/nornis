using Nornis.Infrastructure.Bibliotaph;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Bibliotaph;

[TestFixture]
public class BibliotaphTokenProtectorTests
{
    [Test]
    public void A_protected_token_reads_back_and_is_not_the_token()
    {
        var sut = BibliotaphTokenProtector.Create(null);

        var protectedToken = sut.Protect("v1.refresh-token");

        Assert.That(protectedToken, Does.Not.Contain("refresh-token"));
        Assert.That(sut.Unprotect(protectedToken), Is.EqualTo("v1.refresh-token"));
    }

    [Test]
    public void Another_ring_cannot_read_it()
    {
        var protectedToken = BibliotaphTokenProtector.Create(null).Protect("v1.refresh-token");

        Assert.That(BibliotaphTokenProtector.Create(null).Unprotect(protectedToken), Is.Null);
    }

    [Test]
    public void Tampered_ciphertext_is_unreadable_not_an_exception()
    {
        var sut = BibliotaphTokenProtector.Create(null);
        var protectedToken = sut.Protect("v1.refresh-token");

        Assert.That(sut.Unprotect(protectedToken[..^4] + "AAAA"), Is.Null);
    }
}
