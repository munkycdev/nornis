using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class WorldMemberDisplayNameTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private InMemoryWorldMemberRepository _memberRepository = null!;
    private WorldMemberService _sut = null!;
    private WorldMember _member = null!;

    [SetUp]
    public async Task SetUp()
    {
        _memberRepository = new InMemoryWorldMemberRepository();
        _sut = new WorldMemberService(_memberRepository, new InMemoryUserRepository());

        _member = await _memberRepository.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = Guid.NewGuid(),
            Role = WorldRole.Player,
            DisplayName = null,
            JoinedAt = DateTimeOffset.UtcNow
        });
    }

    [Test]
    public async Task UpdateDisplayNameAsync_SetsTrimmedName()
    {
        var result = await _sut.UpdateDisplayNameAsync(WorldId, _member.UserId, "  Dave  ", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.DisplayName, Is.EqualTo("Dave"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task UpdateDisplayNameAsync_EmptyClearsTheName(string? input)
    {
        _member.DisplayName = "Old Name";

        var result = await _sut.UpdateDisplayNameAsync(WorldId, _member.UserId, input, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.DisplayName, Is.Null);
    }

    [Test]
    public async Task UpdateDisplayNameAsync_Over200Chars_Returns400()
    {
        var result = await _sut.UpdateDisplayNameAsync(WorldId, _member.UserId, new string('x', 201), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task UpdateDisplayNameAsync_NonMember_Returns404()
    {
        var result = await _sut.UpdateDisplayNameAsync(WorldId, Guid.NewGuid(), "Ghost", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }
}
