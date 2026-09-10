using Nornis.Domain.Entities;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class ShelfLinkTests
{
    private static ShelfLink Link(DateTimeOffset? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        PlayerId = Guid.NewGuid(),
        Code = "code",
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
        RevokedAt = revokedAt,
    };

    [Test]
    public void IsActive_WhenNotRevoked_IsTrue()
    {
        Assert.That(Link().IsActive, Is.True);
    }

    [Test]
    public void IsActive_WhenRevoked_IsFalse()
    {
        Assert.That(Link(revokedAt: DateTimeOffset.UtcNow).IsActive, Is.False);
    }

    [Test]
    public void IsActive_HasNoExpiry_OldLinkStillOpens()
    {
        // Revocation is the one control: a link minted long ago is as good as one minted now.
        var link = Link();
        link.CreatedAt = DateTimeOffset.UtcNow.AddYears(-3);

        Assert.That(link.IsActive, Is.True);
    }
}
