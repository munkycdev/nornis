using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class WorldDeletionServiceTests
{
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private FakeBlobStorageService _blobs = null!;
    private WorldDeletionService _sut = null!;
    private World _world = null!;
    private Guid _gmId;
    private Guid _playerId;

    [SetUp]
    public void SetUp()
    {
        _worlds = new InMemoryWorldRepository();
        _members = new InMemoryWorldMemberRepository();
        _blobs = new FakeBlobStorageService();
        _sut = new WorldDeletionService(_worlds, _members, _blobs, NullLogger<WorldDeletionService>.Instance);

        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor",
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
        }).GetAwaiter().GetResult();
        _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = _playerId,
            Role = WorldRole.Player,
            JoinedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
    }

    private DeleteWorldCommand Command(string? confirmationName, Guid? actingUserId = null) =>
        new(_world.Id, actingUserId ?? _gmId, confirmationName);

    [Test]
    public async Task Delete_GmWithExactName_DeletesWorldAndBlobs()
    {
        _blobs.Blobs[$"worlds/{_world.Id}/library/{Guid.NewGuid()}/map.pdf"] = ([1, 2, 3], "application/pdf");

        var result = await _sut.DeleteAsync(Command("Black Harbor"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_worlds.Worlds, Is.Empty);
        Assert.That(_blobs.DeletedPrefixes, Is.EqualTo(new[] { $"worlds/{_world.Id}/" }));
        Assert.That(_blobs.Blobs, Is.Empty);
    }

    [Test]
    public async Task Delete_NameWithSurroundingWhitespace_Succeeds()
    {
        var result = await _sut.DeleteAsync(Command("  Black Harbor  "), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_worlds.Worlds, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("black harbor", Description = "case must match exactly")]
    [TestCase("Black Harbo")]
    public async Task Delete_WrongConfirmationName_Returns400AndKeepsWorld(string? typed)
    {
        var result = await _sut.DeleteAsync(Command(typed), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("confirmation_mismatch"));
        Assert.That(_worlds.Worlds, Has.Count.EqualTo(1));
        Assert.That(_blobs.DeletedPrefixes, Is.Empty);
    }

    [Test]

    [Category("Authorization")]
    public async Task Delete_AsPlayer_Returns403()
    {
        var result = await _sut.DeleteAsync(Command("Black Harbor", _playerId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_worlds.Worlds, Has.Count.EqualTo(1));
    }

    [Test]

    [Category("Authorization")]
    public async Task Delete_AsNonMember_Returns403()
    {
        var result = await _sut.DeleteAsync(Command("Black Harbor", Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Delete_WorldGoneButMembershipLingers_Returns404()
    {
        await _worlds.DeleteAsync(_world.Id);

        var result = await _sut.DeleteAsync(Command("Black Harbor"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Delete_BlobCleanupFails_StillReportsSuccess()
    {
        _blobs.FailDeletes = true;

        var result = await _sut.DeleteAsync(Command("Black Harbor"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, "the DB wipe already committed; blob cleanup is best-effort");
        Assert.That(_worlds.Worlds, Is.Empty);
    }
}
