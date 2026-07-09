using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 1: Invalid Questions Are Rejected
///
/// For any string that is empty, composed entirely of whitespace, or exceeds 2000 characters,
/// the LoremasterService SHALL reject the question and return a validation error without
/// invoking the AI client or the knowledge retriever.
///
/// **Validates: Requirements 2.2**
/// </summary>
[TestFixture]
[Category("Feature: ask-loremaster, Property 1: Invalid Questions Are Rejected")]
public class InvalidQuestionsAreRejectedTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidQuestionArbitraries)],
        MaxTest = 100)]
    [Description("Feature: ask-loremaster, Property 1: Invalid Questions Are Rejected")]
    public void AskAsync_InvalidQuestion_ReturnsValidationError_WithoutInvokingDependencies(
        InvalidQuestionScenario scenario)
    {
        // Arrange
        var knowledgeRetriever = new FakeKnowledgeRetriever();
        var aiClient = new FakeLoremasterAiClient();
        var usageRepo = new InMemoryAiUsageRecordRepository();
        var options = Options.Create(new LoremasterOptions
        {
            MaxQuestionLength = 2000,
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30
        });

        var service = new LoremasterService(
            knowledgeRetriever,
            aiClient,
            usageRepo,
            new FakeAiBudgetGuard(), options);

        var command = new AskLoremasterCommand(
            CampaignId: scenario.CampaignId,
            Question: scenario.InvalidQuestion,
            UserId: scenario.UserId,
            UserRole: scenario.UserRole,
            ConversationContext: null);

        // Act
        var result = service.AskAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert — returns a validation error (400)
        Assert.That(result.IsSuccess, Is.False,
            $"Expected validation failure for question type '{scenario.QuestionCategory}' but got success.");
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400),
            $"Expected HTTP 400 for invalid question but got {result.Error.StatusCode}.");
        Assert.That(result.Error.Code, Is.EqualTo("invalid_question"));

        // Assert — AI client was never called
        Assert.That(aiClient.CallCount, Is.EqualTo(0),
            $"AI client should not be invoked for invalid question (type: {scenario.QuestionCategory}).");

        // Assert — knowledge retriever was never called
        Assert.That(knowledgeRetriever.CallCount, Is.EqualTo(0),
            $"Knowledge retriever should not be invoked for invalid question (type: {scenario.QuestionCategory}).");

        // Assert — no usage records created
        Assert.That(usageRepo.Records.Count, Is.EqualTo(0),
            "No AiUsageRecord should be created for validation failures.");
    }
}

/// <summary>
/// Input model for invalid question scenarios.
/// </summary>
public record InvalidQuestionScenario(
    Guid CampaignId,
    Guid UserId,
    CampaignRole UserRole,
    string InvalidQuestion,
    string QuestionCategory);

/// <summary>
/// Custom FsCheck arbitraries for generating invalid questions:
/// - Empty strings
/// - Whitespace-only strings
/// - Strings exceeding 2000 characters
/// </summary>
public class InvalidQuestionArbitraries
{
    public static Arbitrary<InvalidQuestionScenario> InvalidQuestionScenarios()
    {
        var roleGen = Gen.Elements(
            CampaignRole.GM,
            CampaignRole.Player,
            CampaignRole.Observer);

        var emptyStringGen = Gen.Constant(string.Empty)
            .Select(q => (Question: q, Category: "empty"));

        // Whitespace-only: tabs, spaces, newlines, mixed whitespace of varying lengths
        var whitespaceChars = new[] { ' ', '\t', '\n', '\r', '\u00A0' };
        var whitespaceGen =
            from length in Gen.Choose(1, 50)
            from chars in Gen.Elements(whitespaceChars).ArrayOf(length)
            select (Question: new string(chars), Category: "whitespace-only");

        // Over 2000 characters: generate strings between 2001 and 3000 characters
        var overLengthGen =
            from length in Gen.Choose(2001, 3000)
            from chars in Gen.Elements(
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
                'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
                'u', 'v', 'w', 'x', 'y', 'z', ' ', '?', '!', '.')
                .ArrayOf(length)
            select (Question: new string(chars), Category: "over-2000-characters");

        // Combine all three categories with frequency weighting
        var invalidQuestionGen = Gen.Frequency(
            (1, emptyStringGen),
            (3, whitespaceGen),
            (3, overLengthGen));

        var gen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from role in roleGen
            from invalidQuestion in invalidQuestionGen
            select new InvalidQuestionScenario(
                campaignId,
                userId,
                role,
                invalidQuestion.Question,
                invalidQuestion.Category);

        return gen.ToArbitrary();
    }
}
