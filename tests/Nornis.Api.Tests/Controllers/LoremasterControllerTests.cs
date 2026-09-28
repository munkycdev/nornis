using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Controllers;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Api.Tests.Infrastructure;
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
    private static readonly Guid WorldId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid KeldaUserId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid TavrinUserId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private ILoremasterService _loremasterService = null!;
    private LoremasterController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _loremasterService = Substitute.For<ILoremasterService>();
        _controller = new LoremasterController(_loremasterService, Substitute.For<ISuggestionService>());

        // Set up HttpContext with Kelda (GM) as default user
        SetupHttpContext(KeldaUserId, "Kelda", WorldRole.GM);
    }

    private void SetupHttpContext(Guid userId, string username, WorldRole role)
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
        httpContext.Items["WorldMember"] = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(Streamed(answer)));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        var (status, stream) = await ExecuteAsync(result);
        Assert.That(status, Is.EqualTo(200));
        Assert.That(stream.EventTypes, Is.EqualTo(["start", "delta", "delta", "done"]));
        Assert.That(string.Concat(stream.Deltas), Is.EqualTo(answer.AnswerText));

        var response = stream.Done;
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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(Streamed(answer)));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        var (_, stream) = await ExecuteAsync(result);

        var response = stream.Done;
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
        SetupHttpContext(TavrinUserId, "Tavrin", WorldRole.Player);
        var request = new AskLoremasterRequest("What happened at Black Harbor?", "Previous question about Captain Voss");

        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(Streamed(CreateSimpleAnswer())));

        // Act
        await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        await _loremasterService.Received(1).BeginAskAsync(
            Arg.Is<AskLoremasterCommand>(cmd =>
                cmd != null &&
                cmd.WorldId == WorldId &&
                cmd.Question == "What happened at Black Harbor?" &&
                cmd.UserId == TavrinUserId &&
                cmd.UserRole == WorldRole.Player &&
                cmd.ConversationContext == "Previous question about Captain Voss"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Membership Enforcement via WorldMemberActionFilter

    [Test]
    public void Controller_HasServiceFilterAttribute_ForWorldMemberActionFilter()
    {
        // Verify that [ServiceFilter(typeof(WorldMemberActionFilter))] is present on the controller
        var attributes = typeof(LoremasterController)
            .GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true)
            .Cast<ServiceFilterAttribute>()
            .ToList();

        Assert.That(attributes, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(
            attributes.Any(a => a.ServiceType == typeof(WorldMemberActionFilter)),
            Is.True,
            "LoremasterController must have [ServiceFilter(typeof(WorldMemberActionFilter))]");
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
        Assert.That(routeAttribute!.Template, Is.EqualTo("api/worlds/{worldId:guid}/ask"));
    }

    #endregion

    #region Optional ConversationContext

    [Test]
    public async Task Ask_WithNullConversationContext_Succeeds()
    {
        // Arrange
        var request = new AskLoremasterRequest("Who is Captain Voss?", null);

        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(Streamed(CreateSimpleAnswer())));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        var (status, stream) = await ExecuteAsync(result);
        Assert.That(status, Is.EqualTo(200));
        Assert.That(stream.Done, Is.Not.Null);

        await _loremasterService.Received(1).BeginAskAsync(
            Arg.Is<AskLoremasterCommand>(cmd => cmd != null && cmd.ConversationContext == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Ask_WithConversationContext_PassesContextToService()
    {
        // Arrange
        var conversationContext = "Previously asked about the Missing Caravan investigation.";
        var request = new AskLoremasterRequest("Any updates on that?", conversationContext);

        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(Streamed(CreateSimpleAnswer())));

        // Act
        await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        await _loremasterService.Received(1).BeginAskAsync(
            Arg.Is<AskLoremasterCommand>(cmd =>
                cmd != null && cmd.ConversationContext == conversationContext),
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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(400, "invalid_question", "Question must not be empty.")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        var badRequestResult = result as ObjectResult;
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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(400, "invalid_question", "Question must not exceed 2000 characters.")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        var badRequestResult = result as ObjectResult;
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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again.")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(429, "rate_limited", "Too many requests. Please try again in a moment.")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(500, "retrieval_failed", "NullReferenceException at Nornis.Infrastructure...")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

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
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(500, "internal_error",
                    "at Nornis.Application.Services.LoremasterService.AskAsync() in D:\\repos\\nornis\\src\\LoremasterService.cs:line 47")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

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
    public async Task Ask_UnknownStatusCode_SanitizesBody()
    {
        // Arrange — an unexpected status code that doesn't match 400, 429, or 503.
        // The status passes through (it may carry retry semantics); the body must
        // still be scrubbed.
        var request = new AskLoremasterRequest("Who is Captain Voss?");

        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Fail(
                new AppError(502, "bad_gateway", "Upstream service failed with detailed internal error.")));

        // Act
        var result = await _controller.Ask(WorldId, request, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(502));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
        // Verify the original message doesn't leak
        Assert.That(errorResponse.Message, Does.Not.Contain("Upstream"));
    }

    #endregion

    #region Failure after the answer started → error event

    [Test]
    public async Task Ask_StartAnnouncesConfidenceAndCaveatsBeforeAnyText()
    {
        var answer = new LoremasterAnswer
        {
            AnswerText = "I don't have a confirmed source for that yet.",
            Citations = [],
            Confidence = ConfidenceLevel.Low,
            Caveats = ["Limited information available"],
        };
        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(Streamed(answer)));

        var (_, stream) = await ExecuteAsync(
            await _controller.Ask(WorldId, new AskLoremasterRequest("Who is Captain Voss?"), CancellationToken.None));

        Assert.That(stream.EventTypes.First(), Is.EqualTo("start"));
        Assert.That(stream.Start!.Confidence, Is.EqualTo("Low"));
        Assert.That(stream.Start.Caveats, Is.EqualTo(["Limited information available"]));
    }

    [Test]
    public async Task Ask_FailureAfterText_EndsTheStreamWithAnErrorEventNotDone()
    {
        // The status line has already gone out as 200, so the failure rides in-band. The reader
        // must be told the answer is incomplete rather than left holding half of one.
        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(FailingAfter(
                "Captain Voss sails ",
                new AppError(503, "service_unavailable", "The Loremaster is temporarily unavailable. Please try again."))));

        var (status, stream) = await ExecuteAsync(
            await _controller.Ask(WorldId, new AskLoremasterRequest("Who is Captain Voss?"), CancellationToken.None));

        Assert.That(status, Is.EqualTo(200));
        Assert.That(stream.EventTypes, Is.EqualTo(["start", "delta", "error"]));
        Assert.That(stream.Deltas, Is.EqualTo(["Captain Voss sails "]));
        Assert.That(stream.Error!.Code, Is.EqualTo("service_unavailable"));
        Assert.That(stream.Error.Message, Is.EqualTo("The Loremaster is temporarily unavailable. Please try again."));
    }

    [Test]
    public async Task Ask_InternalFailureAfterText_IsSanitizedInTheErrorEvent()
    {
        // The in-band error goes through the same scrub as a status-code error: an internal
        // message never reaches the reader just because it arrived late.
        _loremasterService
            .BeginAskAsync(Arg.Any<AskLoremasterCommand>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<LoremasterAnswerStream>.Success(FailingAfter(
                "Captain Voss sails ",
                new AppError(500, "retrieval_failed", "NullReferenceException at Nornis.Infrastructure..."))));

        var (_, stream) = await ExecuteAsync(
            await _controller.Ask(WorldId, new AskLoremasterRequest("Who is Captain Voss?"), CancellationToken.None));

        Assert.That(stream.Error!.Code, Is.EqualTo("internal_error"));
        Assert.That(stream.Error.Message, Is.EqualTo("Something went wrong. Please try again."));
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

    /// <summary>The answer as the service streams it: two deltas, then Completed.</summary>
    private static LoremasterAnswerStream Streamed(LoremasterAnswer answer) => new()
    {
        Confidence = answer.Confidence,
        Caveats = answer.Caveats,
        Events = StreamEvents(answer),
    };

    private static async IAsyncEnumerable<LoremasterStreamEvent> StreamEvents(LoremasterAnswer answer)
    {
        await Task.Yield();
        var half = answer.AnswerText.Length / 2;
        yield return new LoremasterStreamEvent.Delta(answer.AnswerText[..half]);
        yield return new LoremasterStreamEvent.Delta(answer.AnswerText[half..]);
        yield return new LoremasterStreamEvent.Completed(answer);
    }

    /// <summary>A stream that delivers <paramref name="text"/> and then fails.</summary>
    private static LoremasterAnswerStream FailingAfter(string text, AppError error) => new()
    {
        Confidence = ConfidenceLevel.Medium,
        Caveats = [],
        Events = FailingEvents(text, error),
    };

    private static async IAsyncEnumerable<LoremasterStreamEvent> FailingEvents(string text, AppError error)
    {
        await Task.Yield();
        yield return new LoremasterStreamEvent.Delta(text);
        yield return new LoremasterStreamEvent.Failed(error);
    }

    /// <summary>
    /// Runs a success result against a real response, as the framework would, and reads the
    /// body back as the event stream the client sees.
    /// </summary>
    private static async Task<(int Status, AskStream Stream)> ExecuteAsync(IActionResult result)
    {
        Assert.That(result, Is.InstanceOf<ServerSentEventsActionResult>(),
            "a successful ask must answer as a stream, not a buffered JSON body");

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await result.ExecuteResultAsync(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

        Assert.That(httpContext.Response.ContentType, Does.StartWith("text/event-stream"));
        body.Position = 0;
        return (httpContext.Response.StatusCode, await AskStream.ParseAsync(body));
    }

    #endregion
}
