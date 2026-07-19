using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Controllers;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

[TestFixture]
public class InvitesControllerTests
{
    private static readonly Guid WorldId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid UserId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    private IWorldInviteService _inviteService = null!;
    private InvitesController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _inviteService = Substitute.For<IWorldInviteService>();
        _controller = new InvitesController(_inviteService);

        // Redemption is NOT world-scoped: only the Nornis user is present, never a WorldMember.
        var httpContext = new DefaultHttpContext();
        httpContext.Items["NornisUser"] = new User
        {
            Id = UserId,
            Auth0SubjectId = $"auth0|{UserId}",
            Username = "Newcomer",
            Email = "newcomer@nornis.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    // ------------------------------------------------------------------- Preview --

    [Test]
    public async Task Preview_ValidCode_Returns200WithWorldAndRole()
    {
        _inviteService.PreviewAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(AppResult<InvitePreview>.Success(
                new InvitePreview(WorldId, "Black Harbor", WorldRole.Player, InviteStatus.Active)));

        var result = await _controller.Preview("abc123", CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var body = ok!.Value as InvitePreviewResponse;
        Assert.That(body!.WorldName, Is.EqualTo("Black Harbor"));
        Assert.That(body.Role, Is.EqualTo("Player"));
        Assert.That(body.Status, Is.EqualTo("Active"));
    }

    [Test]
    public async Task Preview_UnknownCode_Returns404()
    {
        _inviteService.PreviewAsync("nope", Arg.Any<CancellationToken>())
            .Returns(AppResult<InvitePreview>.Fail(new AppError(404, "not_found", "This invite link is not valid.")));

        var result = await _controller.Preview("nope", CancellationToken.None);

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    // -------------------------------------------------------------------- Accept --

    [Test]
    public async Task Accept_ValidCode_Returns200AndPassesCallingUser()
    {
        _inviteService.RedeemAsync("abc123", UserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<InviteRedemption>.Success(
                new InviteRedemption(WorldId, "Black Harbor", AlreadyMember: false)));

        var result = await _controller.Accept("abc123", CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var body = ok!.Value as AcceptInviteResponse;
        Assert.That(body!.WorldId, Is.EqualTo(WorldId));
        Assert.That(body.AlreadyMember, Is.False);
    }

    [Test]
    public async Task Accept_AlreadyMember_Returns200WithFlag()
    {
        _inviteService.RedeemAsync("abc123", UserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<InviteRedemption>.Success(
                new InviteRedemption(WorldId, "Black Harbor", AlreadyMember: true)));

        var result = await _controller.Accept("abc123", CancellationToken.None);

        var body = (result as OkObjectResult)!.Value as AcceptInviteResponse;
        Assert.That(body!.AlreadyMember, Is.True);
    }

    [Test]
    public async Task Accept_ExpiredInvite_Returns409()
    {
        _inviteService.RedeemAsync("abc123", UserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<InviteRedemption>.Fail(new AppError(409, "invite_expired", "This invite has expired.")));

        var result = await _controller.Accept("abc123", CancellationToken.None);

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
    }
}
