using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Controllers;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

[TestFixture]
public class LoremasterControllerTests
{
    private static readonly Guid CampaignId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid KeldaUserId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid TavrinUserId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private ILoremasterService _loremasterService = null!;
    private LoremasterController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _loremasterService = Substitute.For<ILoremasterService>();
        _controller = new LoremasterController(_loremasterService);

        // Set up HttpContext with Kelda (GM) as default user
        SetupHttpContext(KeldaUserId, "Kelda", CampaignRole.GM);
    }

    private void SetupHttpContext(Guid userId, string username, CampaignRole role)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["NornisUser"] = new User
        {
            Id = userId,
            Auth0SubjectId = $"auth0|{userId}",
            Username = username,
            Email = $"{username.ToLower()}@nornis.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        httpContext.Items["CampaignMember"] = new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = CampaignId,
            UserId = userId,
            Role = role,
            DisplayName = username,
            JoinedAt = DateTimeOffset.UtcNow
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #region Valid Request → 200 with AskAnswerResponse

    [Test]
    public async Task Ask_ValidRequest_Returns200WithAskAnswerResponse()
    {
        // Arrange
        var request = new AskLoremasterRequest("Who is Captain Voss?");
        var factId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        var answer = new LoremasterAnswer
        {
            AnswerText = "Captain Voss is a sea captain in Black Harbor.",
            Citations = new List<Citation>
            {
                new()
                {
                    ReferenceId = "art-1",
                    Type = CitationType.Artifact,
                    DisplayName = "Captain Voss",
                    ArtifactId = artifactId,
                    FactId = null,
                    RelationshipId = null,
                    SourceId = null
                },
                new()
                {
                    ReferenceId = "fact-1",
                    Type = CitationType.Fact,
                    DisplayName = "Location: Black Harbor",
                    ArtifactId = null,
                    FactId = factId,
                    RelationshipId = null,
                    SourceId = null
                }
            },
            Confidence = ConfidenceLevel.High,
            Caveats = new List<string> { "Some information is marked as rumor." }
        };

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Success(answer));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var response = okResult.Value as AskAnswerResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Answer, Is.EqualTo("Captain Voss is a sea captain in Black Harbor."));
        Assert.That(response.Confidence, Is.EqualTo("High"));
        Assert.That(response.Citations, Has.Count.EqualTo(2));
        Assert.That(response.Caveats, Has.Count.EqualTo(1));
        Assert.That(response.Caveats[0], Is.EqualTo("Some information is marked as rumor."));
    }

    [Test]
    public async Task Ask_ValidRequest_MapsAnswerCitationsCorrectly()
    {
        // Arrange
        var request = new AskLoremasterRequest("Where is the Silver Key?");
        var sourceId = Guid.NewGuid();

        var answer = new LoremasterAnswer
        {
            AnswerText = "The Silver Key was found in Voss's quarters [ref:src-1].",
            Citations = new List<Citation>
            {
                new()
                {
                    ReferenceId = "src-1",
                    Type = CitationType.Source,
                    DisplayName = "Session 4 — Questioning Captain Voss",
                    ArtifactId = null,
                    FactId = null,
                    RelationshipId = null,
                    SourceId = sourceId
                }
            },
            Confidence = ConfidenceLevel.Medium,
            Caveats = new List<string>()
        };

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Success(answer));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var response = okResult!.Value as AskAnswerResponse;
        Assert.That(response, Is.Not.Null);

        var citation = response!.Citations[0];
        Assert.That(citation.ReferenceId, Is.EqualTo("src-1"));
        Assert.That(citation.Type, Is.EqualTo("Source"));
        Assert.That(citation.DisplayName, Is.EqualTo("Session 4 — Questioning Captain Voss"));
        Assert.That(citation.SourceId, Is.EqualTo(sourceId));
        Assert.That(citation.ArtifactId, Is.Null);
        Assert.That(citation.FactId, Is.Null);
        Assert.That(citation.RelationshipId, Is.Null);
    }

    [Test]
    public async Task Ask_ValidRequest_PassesCorrectCommandToService()
    {
        // Arrange
        SetupHttpContext(TavrinUserId, "Tavrin", CampaignRole.Player);
        var request = new AskLoremasterRequest("What happened at Black Harbor?", "Previous question about Captain Voss");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Success(CreateSimpleAnswer()));

        // Act
        await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        await _loremasterService.Received(1).AskAsync(
            Arg.Is<AskLoremasterCommand>(cmd =>
                cmd.CampaignId == CampaignId &&
                cmd.Question == "What happened at Black Harbor?" &&
                cmd.UserId == TavrinUserId &&
                cmd.UserRole == CampaignRole.Player &&
                cmd.ConversationContext == "Previous question about Captain Voss"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Membership Enforcement via CampaignMemberActionFilter

    [Test]
    public void Controller_HasServiceFilterAttribute_ForCampaignMemberActionFilter()
    {
        // Verify that [ServiceFilter(typeof(CampaignMemberActionFilter))] is present on the controller
        var attributes = typeof(LoremasterController)
            .GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true)
            .Cast<ServiceFilterAttribute>()
            .ToList();

        Assert.That(attributes, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(
            attributes.Any(a => a.ServiceType == typeof(CampaignMemberActionFilter)),
            Is.True,
            "LoremasterController must have [ServiceFilter(typeof(CampaignMemberActionFilter))]");
    }

    [Test]
    public void Controller_HasApiControllerAttribute()
    {
        var hasAttribute = Attribute.IsDefined(typeof(LoremasterController), typeof(ApiControllerAttribute));
        Assert.That(hasAttribute, Is.True, "LoremasterController must have [ApiController] attribute");
    }

    [Test]
    public void Controller_HasCorrectRouteAttribute()
    {
        var routeAttribute = typeof(LoremasterController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .FirstOrDefault();

        Assert.That(routeAttribute, Is.Not.Null);
        Assert.That(routeAttribute!.Template, Is.EqualTo("api/campaigns/{campaignId:guid}/ask"));
    }

    #endregion

    #region Optional ConversationContext

    [Test]
    public async Task Ask_WithNullConversationContext_Succeeds()
    {
        // Arrange
        var request = new AskLoremasterRequest("Who is Captain Voss?", null);

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Success(CreateSimpleAnswer()));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        await _loremasterService.Received(1).AskAsync(
            Arg.Is<AskLoremasterCommand>(cmd => cmd.ConversationContext == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Ask_WithConversationContext_PassesContextToService()
    {
        // Arrange
        var conversationContext = "Previously asked about the Missing Caravan investigation.";
        var request = new AskLoremasterRequest("Any updates on that?", conversationContext);

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Success(CreateSimpleAnswer()));

        // Act
        await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        await _loremasterService.Received(1).AskAsync(
            Arg.Is<AskLoremasterCommand>(cmd =>
                cmd.ConversationContext == conversationContext),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Validation Error → 400

    [Test]
    public async Task Ask_ValidationError_Returns400WithUserFriendlyMessage()
    {
        // Arrange
        var request = new AskLoremasterRequest("");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(400, "invalid_question", "Question must not be empty.")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var errorResponse = badRequestResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("invalid_question"));
        Assert.That(errorResponse.Message, Is.EqualTo("Question must not be empty."));
    }

    [Test]
    public async Task Ask_QuestionTooLong_Returns400WithDescriptiveMessage()
    {
        // Arrange
        var request = new AskLoremasterRequest(new string('x', 2001));

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(400, "invalid_question", "Question must not exceed 2000 characters.")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);

        var errorResponse = badRequestResult!.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("invalid_question"));
        Assert.That(errorResponse.Message, Is.EqualTo("Question must not exceed 2000 characters."));
    }

    #endregion

    #region Service Unavailable → 503

    [Test]
    public async Task Ask_ServiceUnavailable_Returns503WithGenericMessage()
    {
        // Arrange
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again.")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(503));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("service_unavailable"));
        Assert.That(errorResponse.Message, Is.EqualTo("The Loremaster is temporarily unavailable. Please try again."));
    }

    #endregion

    #region Rate Limited → 429

    [Test]
    public async Task Ask_RateLimited_Returns429WithRetryMessage()
    {
        // Arrange
        var request = new AskLoremasterRequest("What do we know about Black Harbor?");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(429, "rate_limited", "Too many requests. Please try again in a moment.")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(429));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("rate_limited"));
        Assert.That(errorResponse.Message, Is.EqualTo("Too many requests. Please try again in a moment."));
    }

    #endregion

    #region Internal Error → 500, No Stack Traces

    [Test]
    public async Task Ask_InternalError_Returns500WithGenericMessage()
    {
        // Arrange
        var request = new AskLoremasterRequest("Tell me about the Missing Caravan.");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(500, "retrieval_failed", "NullReferenceException at Nornis.Infrastructure...")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
    }

    [Test]
    public async Task Ask_InternalError_NeverExposesStackTrace()
    {
        // Arrange
        var request = new AskLoremasterRequest("What about the Silver Key?");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(500, "internal_error",
                    "at Nornis.Application.Services.LoremasterService.AskAsync() in D:\\repos\\nornis\\src\\LoremasterService.cs:line 47")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);

        var errorResponse = objectResult!.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);

        // The original stack trace must NOT leak through
        Assert.That(errorResponse!.Message, Does.Not.Contain("LoremasterService"));
        Assert.That(errorResponse.Message, Does.Not.Contain(":line"));
        Assert.That(errorResponse.Message, Does.Not.Contain("Nornis.Application"));
        Assert.That(errorResponse.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
    }

    [Test]
    public async Task Ask_UnknownStatusCode_Returns500WithGenericMessage()
    {
        // Arrange — an unexpected status code that doesn't match 400, 429, or 503
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        _loremasterService
            .AskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswer>.Fail(
                new AppError(502, "bad_gateway", "Upstream service failed with detailed internal error.")));

        // Act
        var result = await _controller.Ask(CampaignId, request, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
        // Verify the original message doesn't leak
        Assert.That(errorResponse.Message, Does.Not.Contain("Upstream"));
    }

    #endregion

    #region Helpers

    private static LoremasterAnswer CreateSimpleAnswer()
    {
        return new LoremasterAnswer
        {
            AnswerText = "Captain Voss is a sea captain operating out of Black Harbor.",
            Citations = new List<Citation>(),
            Confidence = ConfidenceLevel.Medium,
            Caveats = new List<string>()
        };
    }

    #endregion
}
