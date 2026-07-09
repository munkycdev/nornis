using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nornis.Api.Controllers;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

[TestFixture]
public class CostsControllerTests
{
    private static readonly Guid WorldId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid KeldaUserId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid TavrinUserId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private ICostService _costService = null!;
    private CostsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _costService = Substitute.For<ICostService>();
        var logger = Substitute.For<ILogger<CostsController>>();
        _controller = new CostsController(_costService, logger, Microsoft.Extensions.Options.Options.Create(new Nornis.Application.Configuration.AiBudgetOptions()));

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

    #region Valid Summary Request → 200 with TimePeriodSummaryResponse

    [Test]
    public async Task GetSummary_ValidRequest_Returns200WithTimePeriodSummaryResponse()
    {
        // Arrange
        var summary = new TimePeriodCostResult
        {
            Today = new CostSummary
            {
                TotalInputTokens = 100,
                TotalOutputTokens = 50,
                TotalTokens = 150,
                TotalEstimatedCostUsd = 0.01m,
                OperationCount = 2
            },
            ThisWeek = new CostSummary
            {
                TotalInputTokens = 500,
                TotalOutputTokens = 250,
                TotalTokens = 750,
                TotalEstimatedCostUsd = 0.05m,
                OperationCount = 10
            },
            ThisMonth = new CostSummary
            {
                TotalInputTokens = 2000,
                TotalOutputTokens = 1000,
                TotalTokens = 3000,
                TotalEstimatedCostUsd = 0.20m,
                OperationCount = 30
            },
            AllTime = new CostSummary
            {
                TotalInputTokens = 10000,
                TotalOutputTokens = 5000,
                TotalTokens = 15000,
                TotalEstimatedCostUsd = 1.00m,
                OperationCount = 100
            }
        };

        _costService
            .GetSummaryAsync(WorldId, KeldaUserId, WorldRole.GM, Arg.Any<CancellationToken>())
            .Returns(AppResult<TimePeriodCostResult>.Success(summary));

        // Act
        var result = await _controller.GetSummary(WorldId, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var response = okResult.Value as TimePeriodSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Today.TotalInputTokens, Is.EqualTo(100));
        Assert.That(response.Today.TotalOutputTokens, Is.EqualTo(50));
        Assert.That(response.Today.TotalTokens, Is.EqualTo(150));
        Assert.That(response.Today.TotalEstimatedCostUsd, Is.EqualTo(0.01m));
        Assert.That(response.Today.OperationCount, Is.EqualTo(2));
        Assert.That(response.ThisWeek.TotalTokens, Is.EqualTo(750));
        Assert.That(response.ThisMonth.TotalTokens, Is.EqualTo(3000));
        Assert.That(response.AllTime.TotalTokens, Is.EqualTo(15000));
        Assert.That(response.AllTime.OperationCount, Is.EqualTo(100));
    }

    [Test]
    public async Task GetSummary_PassesCorrectParametersToService()
    {
        // Arrange
        SetupHttpContext(TavrinUserId, "Tavrin", WorldRole.Player);

        _costService
            .GetSummaryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<WorldRole>(), Arg.Any<CancellationToken>())
            .Returns(AppResult<TimePeriodCostResult>.Success(CreateEmptyTimePeriodResult()));

        // Act
        await _controller.GetSummary(WorldId, CancellationToken.None);

        // Assert
        await _costService.Received(1).GetSummaryAsync(
            WorldId, TavrinUserId, WorldRole.Player, Arg.Any<CancellationToken>());
    }

    #endregion

    #region WorldMemberActionFilter Applied (non-member → 403)

    [Test]
    public void CostsController_HasServiceFilterAttribute_ForWorldMemberActionFilter()
    {
        var attributes = typeof(CostsController)
            .GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true)
            .Cast<ServiceFilterAttribute>()
            .ToList();

        Assert.That(attributes, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(
            attributes.Any(a => a.ServiceType == typeof(WorldMemberActionFilter)),
            Is.True,
            "CostsController must have [ServiceFilter(typeof(WorldMemberActionFilter))]");
    }

    [Test]
    public void CostsController_HasApiControllerAttribute()
    {
        var hasAttribute = Attribute.IsDefined(typeof(CostsController), typeof(ApiControllerAttribute));
        Assert.That(hasAttribute, Is.True, "CostsController must have [ApiController] attribute");
    }

    [Test]
    public void CostsController_HasCorrectRouteAttribute()
    {
        var routeAttribute = typeof(CostsController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .FirstOrDefault();

        Assert.That(routeAttribute, Is.Not.Null);
        Assert.That(routeAttribute!.Template, Is.EqualTo("api/worlds/{worldId:guid}/costs"));
    }

    #endregion

    #region Invalid Date Range → 400 with Descriptive Message

    [Test]
    public async Task GetByUser_InvalidDateRange_Returns400WithDescriptiveMessage()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow;
        var endDate = startDate.AddDays(-7); // end before start

        _costService
            .GetByUserAsync(WorldId, KeldaUserId, WorldRole.GM, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<UserCostResult>>.Fail(
                new AppError(400, "invalid_date_range", "Start date must be before or equal to end date.")));

        // Act
        var result = await _controller.GetByUser(WorldId, startDate, endDate, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var errorResponse = badRequestResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("invalid_date_range"));
        Assert.That(errorResponse.Message, Is.EqualTo("Start date must be before or equal to end date."));
    }

    [Test]
    public async Task GetByOperation_InvalidDateRange_Returns400WithDescriptiveMessage()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow;
        var endDate = startDate.AddDays(-1);

        _costService
            .GetByOperationTypeAsync(WorldId, KeldaUserId, WorldRole.GM, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<OperationTypeCostResult>>.Fail(
                new AppError(400, "invalid_date_range", "Start date must be before or equal to end date.")));

        // Act
        var result = await _controller.GetByOperation(WorldId, startDate, endDate, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);

        var errorResponse = badRequestResult!.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("invalid_date_range"));
    }

    [Test]
    public async Task GetByModel_InvalidDateRange_Returns400WithDescriptiveMessage()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow;
        var endDate = startDate.AddDays(-3);

        _costService
            .GetByModelAsync(WorldId, KeldaUserId, WorldRole.GM, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<ModelCostResult>>.Fail(
                new AppError(400, "invalid_date_range", "Start date must be before or equal to end date.")));

        // Act
        var result = await _controller.GetByModel(WorldId, startDate, endDate, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);

        var errorResponse = badRequestResult!.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("invalid_date_range"));
    }

    #endregion

    #region Invalid WorldId Format → 404 (Route Constraint)

    [Test]
    public void CostsController_RouteConstraint_RequiresGuidFormat()
    {
        // The route "api/worlds/{worldId:guid}/costs" has a :guid constraint.
        // Non-GUID values will never reach the controller action (ASP.NET Core returns 404 at routing level).
        // We verify the route template declares the constraint.
        var routeAttribute = typeof(CostsController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .FirstOrDefault();

        Assert.That(routeAttribute, Is.Not.Null);
        Assert.That(routeAttribute!.Template, Does.Contain("{worldId:guid}"),
            "Route must include :guid constraint to reject non-GUID worldId values with 404");
    }

    #endregion

    #region Internal Error → 500 with Generic Message, No Stack Traces

    [Test]
    public async Task GetSummary_InternalError_Returns500WithGenericMessage()
    {
        // Arrange
        _costService
            .GetSummaryAsync(WorldId, KeldaUserId, WorldRole.GM, Arg.Any<CancellationToken>())
            .Returns(AppResult<TimePeriodCostResult>.Fail(
                new AppError(500, "aggregation_failed", "NullReferenceException at Nornis.Infrastructure.Persistence...")));

        // Act
        var result = await _controller.GetSummary(WorldId, CancellationToken.None);

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
    public async Task GetSummary_InternalError_NeverExposesStackTrace()
    {
        // Arrange
        _costService
            .GetSummaryAsync(WorldId, KeldaUserId, WorldRole.GM, Arg.Any<CancellationToken>())
            .Returns(AppResult<TimePeriodCostResult>.Fail(
                new AppError(500, "internal_error",
                    "at Nornis.Infrastructure.Persistence.AiUsageRecordRepository.AggregateAsync() in D:\\repos\\nornis\\src:line 123")));

        // Act
        var result = await _controller.GetSummary(WorldId, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);

        var errorResponse = objectResult!.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Message, Does.Not.Contain("Nornis.Infrastructure"));
        Assert.That(errorResponse.Message, Does.Not.Contain(":line"));
        Assert.That(errorResponse.Message, Does.Not.Contain("AggregateAsync"));
        Assert.That(errorResponse.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
    }

    [Test]
    public async Task GetByUser_InternalError_Returns500WithGenericMessage()
    {
        // Arrange
        _costService
            .GetByUserAsync(WorldId, KeldaUserId, WorldRole.GM, null, null, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<UserCostResult>>.Fail(
                new AppError(500, "database_timeout", "SQL timeout after 30 seconds querying AiUsageRecords.")));

        // Act
        var result = await _controller.GetByUser(WorldId, null, null, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
        Assert.That(errorResponse.Message, Does.Not.Contain("SQL"));
        Assert.That(errorResponse.Message, Does.Not.Contain("timeout"));
    }

    [Test]
    public async Task GetSummary_UnknownStatusCode_Returns500WithGenericMessage()
    {
        // Arrange — an unexpected status code from the service
        _costService
            .GetSummaryAsync(WorldId, KeldaUserId, WorldRole.GM, Arg.Any<CancellationToken>())
            .Returns(AppResult<TimePeriodCostResult>.Fail(
                new AppError(502, "upstream_failure", "Detailed internal failure info.")));

        // Act
        var result = await _controller.GetSummary(WorldId, CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));

        var errorResponse = objectResult.Value as ErrorResponse;
        Assert.That(errorResponse, Is.Not.Null);
        Assert.That(errorResponse!.Code, Is.EqualTo("internal_error"));
        Assert.That(errorResponse.Message, Is.EqualTo("Something went wrong. Please try again."));
        Assert.That(errorResponse.Message, Does.Not.Contain("upstream"));
    }

    #endregion

    #region Cross-World Endpoint Accessible Without World-Scoped Filter

    [Test]
    public void CrossWorldCostsController_DoesNotHaveWorldMemberActionFilter()
    {
        // The CrossWorldCostsController should NOT have the WorldMemberActionFilter
        // because it is not scoped to a single world.
        var attributes = typeof(CrossWorldCostsController)
            .GetCustomAttributes(typeof(ServiceFilterAttribute), inherit: true)
            .Cast<ServiceFilterAttribute>()
            .ToList();

        var hasWorldFilter = attributes.Any(a => a.ServiceType == typeof(WorldMemberActionFilter));
        Assert.That(hasWorldFilter, Is.False,
            "CrossWorldCostsController must NOT have WorldMemberActionFilter since it is not world-scoped");
    }

    [Test]
    public void CrossWorldCostsController_HasApiControllerAttribute()
    {
        var hasAttribute = Attribute.IsDefined(typeof(CrossWorldCostsController), typeof(ApiControllerAttribute));
        Assert.That(hasAttribute, Is.True, "CrossWorldCostsController must have [ApiController] attribute");
    }

    [Test]
    public void CrossWorldCostsController_HasCorrectRouteAttribute()
    {
        var routeAttribute = typeof(CrossWorldCostsController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .FirstOrDefault();

        Assert.That(routeAttribute, Is.Not.Null);
        Assert.That(routeAttribute!.Template, Is.EqualTo("api/costs"));
    }

    [Test]
    public async Task CrossWorldCostsController_GetByWorld_Returns200WithResults()
    {
        // Arrange
        var costService = Substitute.For<ICostService>();
        var logger = Substitute.For<ILogger<CrossWorldCostsController>>();
        var controller = new CrossWorldCostsController(costService, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["NornisUser"] = new User
        {
            Id = KeldaUserId,
            Auth0SubjectId = $"auth0|{KeldaUserId}",
            Username = "Kelda",
            Email = "kelda@nornis.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var worldResults = new List<WorldCostResult>
        {
            new()
            {
                WorldId = Guid.NewGuid(),
                WorldName = "Black Harbor Investigation",
                Summary = new CostSummary
                {
                    TotalInputTokens = 5000,
                    TotalOutputTokens = 2500,
                    TotalTokens = 7500,
                    TotalEstimatedCostUsd = 0.50m,
                    OperationCount = 50
                }
            },
            new()
            {
                WorldId = Guid.NewGuid(),
                WorldName = "Silver Key Mystery",
                Summary = new CostSummary
                {
                    TotalInputTokens = 3000,
                    TotalOutputTokens = 1500,
                    TotalTokens = 4500,
                    TotalEstimatedCostUsd = 0.30m,
                    OperationCount = 25
                }
            }
        };

        costService
            .GetByWorldAsync(KeldaUserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<WorldCostResult>>.Success(worldResults));

        // Act
        var result = await controller.GetByWorld(CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));

        var response = okResult.Value as List<WorldCostResponse>;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!, Has.Count.EqualTo(2));
        Assert.That(response![0].WorldName, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(response[1].WorldName, Is.EqualTo("Silver Key Mystery"));
    }

    [Test]
    public async Task CrossWorldCostsController_DoesNotRequireWorldMember()
    {
        // Arrange - set up HttpContext with only the NornisUser (no WorldMember needed)
        var costService = Substitute.For<ICostService>();
        var logger = Substitute.For<ILogger<CrossWorldCostsController>>();
        var controller = new CrossWorldCostsController(costService, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["NornisUser"] = new User
        {
            Id = KeldaUserId,
            Auth0SubjectId = $"auth0|{KeldaUserId}",
            Username = "Kelda",
            Email = "kelda@nornis.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        // Deliberately NOT setting WorldMember in HttpContext

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        costService
            .GetByWorldAsync(KeldaUserId, Arg.Any<CancellationToken>())
            .Returns(AppResult<IReadOnlyList<WorldCostResult>>.Success(
                new List<WorldCostResult>()));

        // Act — should not throw even without WorldMember
        var result = await controller.GetByWorld(CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
    }

    #endregion

    #region Helpers

    private static TimePeriodCostResult CreateEmptyTimePeriodResult()
    {
        return new TimePeriodCostResult
        {
            Today = CostSummary.Empty,
            ThisWeek = CostSummary.Empty,
            ThisMonth = CostSummary.Empty,
            AllTime = CostSummary.Empty
        };
    }

    #endregion
}
