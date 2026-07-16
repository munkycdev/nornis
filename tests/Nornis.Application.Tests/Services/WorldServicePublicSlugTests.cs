using NUnit.Framework;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class WorldServicePublicSlugTests
{
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private WorldService _sut = null!;
    private World _world = null!;
    private Guid _gmId;

    [SetUp]
    public void SetUp()
    {
        _worlds = new InMemoryWorldRepository();
        _members = new InMemoryWorldMemberRepository();
        _sut = new WorldService(_worlds, _members);
        _gmId = Guid.NewGuid();

        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Symbaroum",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _worlds.CreateAsync(_world).GetAwaiter().GetResult();
        _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = _gmId,
            Role = WorldRole.GM,
            JoinedAt = DateTimeOffset.UtcNow,
        });
    }

    private UpdateWorldCommand Command(string? slug = null, bool? enabled = null) =>
        new(_world.Id, null, null, null, _gmId, PublicSlug: slug, PublicAccessEnabled: enabled);

    [Test]
    public async Task Update_ValidSlug_NormalizesToLowercase()
    {
        var result = await _sut.UpdateAsync(Command(slug: "  My-World-42 "), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PublicSlug, Is.EqualTo("my-world-42"));
        Assert.That(result.Value.PublicAccessEnabled, Is.False, "setting a slug does not enable access");
    }

    [TestCase("ab")]
    [TestCase("-leading")]
    [TestCase("trailing-")]
    [TestCase("has space")]
    [TestCase("Ünïcode")]
    [TestCase("under_score")]
    public async Task Update_InvalidSlug_Returns400(string slug)
    {
        var result = await _sut.UpdateAsync(Command(slug: slug), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Update_SlugTakenByAnotherWorld_Returns409()
    {
        await _worlds.CreateAsync(new World
        {
            Id = Guid.NewGuid(),
            Name = "Other",
            PublicSlug = "symbaroum",
            CreatedByUserId = Guid.NewGuid(),
        });

        var result = await _sut.UpdateAsync(Command(slug: "Symbaroum"), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("slug_taken"));
    }

    [Test]
    public async Task Update_ReassertingOwnSlug_Succeeds()
    {
        _world.PublicSlug = "symbaroum";

        var result = await _sut.UpdateAsync(Command(slug: "symbaroum", enabled: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PublicAccessEnabled, Is.True);
    }

    [Test]
    public async Task Update_EnableWithoutSlug_Returns400()
    {
        var result = await _sut.UpdateAsync(Command(enabled: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Update_SlugAndEnableTogether_Succeeds()
    {
        var result = await _sut.UpdateAsync(Command(slug: "symbaroum", enabled: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PublicAccessEnabled, Is.True);
    }

    [Test]
    public async Task Update_Disable_KeepsSlug()
    {
        _world.PublicSlug = "symbaroum";
        _world.PublicAccessEnabled = true;

        var result = await _sut.UpdateAsync(Command(enabled: false), CancellationToken.None);

        Assert.That(result.Value!.PublicAccessEnabled, Is.False);
        Assert.That(result.Value.PublicSlug, Is.EqualTo("symbaroum"), "the slug survives so re-enabling restores the link");
    }
}
