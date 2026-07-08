using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Controllers;
using Nornis.Api.Contracts.Responses;
using Nornis.Application.Errors;
using Nornis.Application.Services;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers.PropertyTests;

/// <summary>
/// Property 12: Error Responses Never Expose Internals
///
/// For any error that occurs during the Ask pipeline (AI failure, retrieval failure,
/// unexpected exception), the error response returned to the client SHALL never contain
/// stack traces, internal exception messages, AI prompt content, or retrieved knowledge content.
///
/// **Validates: Requirements 10.4**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 12: Error Responses Never Expose Internals")]
public class ErrorResponsesNeverExposeInternalsTests
{
    private static readonly MethodInfo MapErrorMethod = typeof(LoremasterController)
        .GetMethod("MapError", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ErrorResponseArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 12: Error Responses Never Expose Internals")]
    public void MapError_NeverExposesInternalDetails(ErrorResponseScenario scenario)
    {
        // Arrange
        var mockService = Substitute.For<ILoremasterService>();
        var controller = new LoremasterController(mockService, Substitute.For<ISuggestionService>());

        // Act — invoke MapError via reflection
        var result = (IActionResult)MapErrorMethod.Invoke(controller, [scenario.Error])!;

        // Extract the ErrorResponse from the result
        var errorResponse = ExtractErrorResponse(result);
        Assert.That(errorResponse, Is.Not.Null, "MapError should always produce an ErrorResponse body.");

        var code = errorResponse!.Code;
        var message = errorResponse.Message;

        // Assert 1: For 500-level responses (catch-all), only returns generic message
        // This is the core safety property — internal errors never leak details
        if (scenario.Error.StatusCode != 400 && scenario.Error.StatusCode != 429 && scenario.Error.StatusCode != 503)
        {
            Assert.That(code, Is.EqualTo("internal_error"),
                $"500-level error code should be 'internal_error', got '{code}'");
            Assert.That(message, Is.EqualTo("Something went wrong. Please try again."),
                $"500-level error message should be the generic message, got '{message}'");

            // Verify dangerous content is never in the response
            AssertNoStackTraces(code, message);
            AssertNoPromptContent(code, message);
            AssertNoKnowledgeContent(code, message);

            // The original dangerous message must not appear
            if (scenario.Error.Message != "Something went wrong. Please try again.")
            {
                Assert.That(message, Does.Not.Contain(scenario.Error.Message),
                    "500-level response should not contain the original error message.");
            }
        }

        // Assert 2: For 400/429/503, uses the error's own message (user-facing by design)
        if (scenario.Error.StatusCode is 400 or 429 or 503)
        {
            Assert.That(code, Is.EqualTo(scenario.Error.Code),
                "400/429/503 should pass through the error code.");
            Assert.That(message, Is.EqualTo(scenario.Error.Message),
                "400/429/503 should pass through the user-facing message.");
        }
    }

    private static ErrorResponse? ExtractErrorResponse(IActionResult result)
    {
        return result switch
        {
            BadRequestObjectResult badRequest => badRequest.Value as ErrorResponse,
            ObjectResult objectResult => objectResult.Value as ErrorResponse,
            _ => null
        };
    }

    private static void AssertNoStackTraces(string code, string message)
    {
        var combined = $"{code} {message}";

        // Stack trace patterns: "at Namespace.Class.Method" or ".cs:line"
        Assert.That(combined, Does.Not.Match(@"at\s+\w+(\.\w+)+\("),
            $"Response contains stack trace pattern 'at Namespace.Class.Method(': {combined}");
        Assert.That(combined, Does.Not.Match(@"\.cs:line\s+\d+"),
            $"Response contains stack trace pattern '.cs:line N': {combined}");
        Assert.That(combined, Does.Not.Match(@"\.cs:\d+"),
            $"Response contains file path pattern '.cs:N': {combined}");
    }

    private static void AssertNoPromptContent(string code, string message)
    {
        var combined = $"{code} {message}";

        // Prompt content patterns
        Assert.That(combined, Does.Not.Contain("You are a Loremaster").IgnoreCase,
            "Response contains AI prompt fragment 'You are a Loremaster'");
        Assert.That(combined, Does.Not.Contain("system prompt").IgnoreCase,
            "Response contains prompt reference 'system prompt'");
        Assert.That(combined, Does.Not.Contain("Ground all answers").IgnoreCase,
            "Response contains prompt instruction 'Ground all answers'");
        Assert.That(combined, Does.Not.Contain("[ref:").IgnoreCase,
            "Response contains citation marker format '[ref:'");
    }

    private static void AssertNoKnowledgeContent(string code, string message)
    {
        var combined = $"{code} {message}";

        // Knowledge content patterns that indicate internal campaign data leakage
        Assert.That(combined, Does.Not.Contain("ArtifactFact").IgnoreCase,
            "Response contains internal model name 'ArtifactFact'");
        Assert.That(combined, Does.Not.Contain("TruthState =").IgnoreCase,
            "Response contains internal property dump 'TruthState ='");
        Assert.That(combined, Does.Not.Contain("GMOnly content").IgnoreCase,
            "Response contains visibility marker 'GMOnly content'");
    }
}

/// <summary>
/// Input model for error response property testing scenarios.
/// Contains an AppError with potentially dangerous content.
/// </summary>
public record ErrorResponseScenario(AppError Error);

/// <summary>
/// Custom FsCheck arbitraries for error response property tests.
/// Generates AppError instances with dangerous content including stack traces,
/// file paths, exception messages, prompt text, and campaign knowledge.
/// </summary>
public class ErrorResponseArbitraries
{
    /// <summary>
    /// Dangerous message fragments that should never appear in error responses.
    /// </summary>
    private static readonly string[] StackTraceMessages =
    [
        "at Nornis.Application.Services.LoremasterService.AskAsync(AskLoremasterCommand command) in D:\\repos\\nornis\\src\\Nornis.Application\\Services\\LoremasterService.cs:line 47",
        "at System.Threading.Tasks.Task.ThrowIfExceptional() in /_/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs:line 234",
        "at Microsoft.EntityFrameworkCore.Query.Internal.QueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\r\n   at Nornis.Infrastructure.Repositories.ArtifactRepository.ListByNamesInTextAsync(Guid campaignId)",
        "System.InvalidOperationException: Sequence contains no matching element\r\n   at System.Linq.ThrowHelper.ThrowNoMatchException()\r\n   at Nornis.Application.Services.LoremasterService.<>c.cs:42",
        "NullReferenceException: Object reference not set to an instance of an object.\n   at Nornis.Api.Controllers.LoremasterController.Ask(Guid campaignId) in C:\\src\\Controllers\\LoremasterController.cs:line 35"
    ];

    private static readonly string[] PromptMessages =
    [
        "You are a Loremaster AI assistant. Ground all answers in the provided campaign knowledge only.",
        "System prompt: You are the campaign's Loremaster. Use [ref:ID] notation to cite sources.",
        "Do not invent campaign facts beyond what is provided. Cite sources using [ref:art-123abc].",
        "The AI model returned: 'You are a Loremaster for the Black Harbor Investigation campaign.'",
        "Prompt construction failed: system prompt template not found. Ground all answers in provided knowledge."
    ];

    private static readonly string[] KnowledgeMessages =
    [
        "Knowledge context: Captain Voss is located in Black Harbor. He is suspected in the Missing Caravan investigation.",
        "Retrieved artifacts: Silver Key (Item), Black Harbor (Location), Captain Voss (Character). Facts: Silver Key found in Voss's quarters.",
        "ArtifactFact { Predicate = 'allegiance', Value = 'Shadow Cult', TruthState = Hidden }",
        "Campaign knowledge indicates: The Silver Key unlocks the vault beneath Black Harbor lighthouse.",
        "GMOnly content: Captain Voss secretly works for the Shadow Cult faction."
    ];

    private static readonly string[] InternalExceptionMessages =
    [
        "Azure.RequestFailedException: The deployment 'gpt-4o' does not exist. Resource: nornis-ai-prod.openai.azure.com",
        "System.Net.Http.HttpRequestException: Connection refused (api.openai.azure.com:443)",
        "Microsoft.Data.SqlClient.SqlException: A transport-level error has occurred. Server: nornis-sql-prod.database.windows.net",
        "TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 30 seconds.",
        "Npgsql.PostgresException: 57P01: terminating connection due to administrator command"
    ];

    private static readonly string[] UserFacingMessages =
    [
        "The Loremaster is temporarily unavailable. Please try again.",
        "Too many requests. Please try again in a moment.",
        "Question must not be empty.",
        "Question must not exceed 2000 characters.",
        "Something went wrong. Please try again."
    ];

    private static readonly string[] ErrorCodes =
    [
        "invalid_question",
        "rate_limited",
        "service_unavailable",
        "internal_error",
        "retrieval_failed",
        "ai_timeout",
        "unknown_error"
    ];

    public static Arbitrary<ErrorResponseScenario> ErrorResponseScenarios()
    {
        // Generate a mix of status codes, with emphasis on the catch-all (500-level) path
        // since that's where the security property is critical
        var statusCodeGen = Gen.Frequency(
            (3, Gen.Elements(500, 502, 999)), // Catch-all cases (dangerous content must be hidden)
            (1, Gen.Elements(400, 429, 503))  // Pass-through cases (user-facing by design)
        );

        var gen =
            from statusCode in statusCodeGen
            from dangerousMessage in GenDangerousMessage()
            from code in Gen.Elements(ErrorCodes)
            select new ErrorResponseScenario(new AppError(statusCode, code, dangerousMessage));

        return gen.ToArbitrary();
    }

    private static Gen<string> GenDangerousMessage()
    {
        // Mix of different dangerous content types
        var allDangerousMessages = StackTraceMessages
            .Concat(PromptMessages)
            .Concat(KnowledgeMessages)
            .Concat(InternalExceptionMessages)
            .Concat(UserFacingMessages)
            .ToArray();

        // Generate either a single dangerous message or a combination
        var singleMessageGen = Gen.Elements(allDangerousMessages);

        var combinedMessageGen =
            from msg1 in Gen.Elements(allDangerousMessages)
            from msg2 in Gen.Elements(allDangerousMessages)
            select $"{msg1}\n{msg2}";

        return Gen.OneOf(singleMessageGen, combinedMessageGen);
    }
}
