using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Controllers;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

[TestFixture]
public class WorldInvitesControllerTests
{
    private static readonly Guid WorldId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid GmUserId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private IWorldInviteService _inviteService = null!;
    private WorldInvitesController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _inviteService = Substitute.For<IWorldInviteService>();
        _controller = new WorldInvitesController(_inviteService);
        SetupHttpContext(GmUserId, WorldRole.GM);
    }

    private void SetupHttpContext(Guid userId, WorldRole role)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["NornisUser"] = new User
        {
            Id = userId,
            Auth0SubjectId = $"auth0|{userId}",
            Username = "Kelda",
            Email = "kelda@nornis.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        httpContext.Items["WorldMember"] = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow
        };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static WorldInvite Invite(WorldRole role = WorldRole.Player) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Code = "abc123",
        Role = role,
        CreatedByUserId = GmUserId,
        CreatedAt = DateTimeOffset.UtcNow,
        UseCount = 0
    };

    // ---------------------------------------------------------------------- List --

    [Test]
    public async Task List_AsGm_Returns200WithInvites()
    {
        _inviteService.ListAsync(WorldId, GmUserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<WorldInvite>>.Success([Invite(), Invite()]));

        var result = await _controller.List(WorldId, CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var invites = ok!.Value as List<WorldInviteResponse>;
        Assert.That(invites, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task List_AsNonGm_Returns403()
    {
        SetupHttpContext(GmUserId, WorldRole.Player);

        var result = await _controller.List(WorldId, CancellationToken.None);

        Assert.That((result as ObjectResult)!.StatusCode, Is.EqualTo(403));
        await _inviteService.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------- Create --

    [Test]
    public async Task Create_AsGm_ValidRole_Returns201()
    {
        _inviteService.CreateAsync(Arg.Any<CreateInviteCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<WorldInvite>.Success(Invite(WorldRole.Observer)));

        var result = await _controller.Create(WorldId, new CreateInviteRequest("Observer"), CancellationToken.None);

        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Create_InvalidRole_Returns400()
    {
        var result = await _controller.Create(WorldId, new CreateInviteRequest("Wizard"), CancellationToken.None);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        await _inviteService.DidNotReceive().CreateAsync(Arg.Any<CreateInviteCommand>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Create_AsNonGm_Returns403()
    {
        SetupHttpContext(GmUserId, WorldRole.Player);

        var result = await _controller.Create(WorldId, new CreateInviteRequest("Player"), CancellationToken.None);

        Assert.That((result as ObjectResult)!.StatusCode, Is.EqualTo(403));
    }

    // -------------------------------------------------------------------- Revoke --

    [Test]
    public async Task Revoke_AsGm_Returns204()
    {
        _inviteService.RevokeAsync(WorldId, Arg.Any<Guid>(), GmUserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<WorldInvite>.Success(Invite()));

        var result = await _controller.Revoke(WorldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.TypeOf<NoContentResult>());
    }

    [Test]
    public async Task Revoke_NotFound_Returns404()
    {
        _inviteService.RevokeAsync(WorldId, Arg.Any<Guid>(), GmUserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<WorldInvite>.Fail(new AppError(404, "not_found", "Invite not found in this world.")));

        var result = await _controller.Revoke(WorldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Revoke_AsNonGm_Returns403()
    {
        SetupHttpContext(GmUserId, WorldRole.Observer);

        var result = await _controller.Revoke(WorldId, Guid.NewGuid(), CancellationToken.None);

        Assert.That((result as ObjectResult)!.StatusCode, Is.EqualTo(403));
    }

    // The world-scoped controller must resolve membership via the action filter.
    [Test]
    public void Controller_HasServiceFilterAttribute_ForWorldMemberActionFilter()
    {
        var attributes = typeof(WorldInvitesController)
            .GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true)
            .Cast<ServiceFilterAttribute>()
            .ToList();

        Assert.That(attributes.Any(a => a.ServiceType == typeof(WorldMemberActionFilter)), Is.True);
    }
}
