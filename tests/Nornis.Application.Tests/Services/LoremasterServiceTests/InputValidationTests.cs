using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.LoremasterServiceTests;

[TestFixture]
public class InputValidationTests
{
    private IKnowledgeRetriever _knowledgeRetriever = null!;
    private ILoremasterAiClient _aiClient = null!;
    private IAiUsageRecordRepository _aiUsageRecordRepository = null!;
    private LoremasterService _service = null!;

    private static readonly Guid CampaignId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid KeldaUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TavrinUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [SetUp]
    public void SetUp()
    {
        _knowledgeRetriever = Substitute.For<IKnowledgeRetriever>();
        _aiClient = Substitute.For<ILoremasterAiClient>();
        _aiUsageRecordRepository = Substitute.For<IAiUsageRecordRepository>();

        var options = new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiTimeoutSeconds = 30,
            MaxRetrievalCount = 30,
            MaxQuestionLength = 2000
        };

        _service = new LoremasterService(
            _knowledgeRetriever,
            _aiClient,
            _aiUsageRecordRepository,
            new FakeAiBudgetGuard(), Options.Create(options));
    }

    [Test]
    public async Task AskAsync_EmptyQuestion_Returns400ValidationError()
    {
        var command = CreateCommand(question: "");

        var result = await _service.AskAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_question"));
    }

    [Test]
    public async Task AskAsync_WhitespaceOnlyQuestion_Returns400ValidationError()
    {
        var command = CreateCommand(question: "   \t  \n  ");

        var result = await _service.AskAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_question"));
    }

    [Test]
    public async Task AskAsync_QuestionOver2000Characters_Returns400ValidationError()
    {
        var longQuestion = new string('A', 2001);
        var command = CreateCommand(question: longQuestion);

        var result = await _service.AskAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_question"));
        Assert.That(result.Error.Message, Does.Contain("2000"));
    }

    [Test]
    public async Task AskAsync_ValidQuestion_ProceedsToRetrieval()
    {
        _knowledgeRetriever.RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateEmptyContext());

        _aiClient.AskAsync(Arg.Any<LoremasterAiRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateDefaultAiResponse());

        var command = CreateCommand(question: "Who is Captain Voss?");

        var result = await _service.AskAsync(command, CancellationToken.None);

        await _knowledgeRetriever.Received(1).RetrieveAsync(
            "Who is Captain Voss?",
            CampaignId,
            KeldaUserId,
            CampaignRole.GM,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_EmptyQuestion_DoesNotCallKnowledgeRetriever()
    {
        var command = CreateCommand(question: "");

        await _service.AskAsync(command, CancellationToken.None);

        await _knowledgeRetriever.DidNotReceive().RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_EmptyQuestion_DoesNotCallAiClient()
    {
        var command = CreateCommand(question: "");

        await _service.AskAsync(command, CancellationToken.None);

        await _aiClient.DidNotReceive().AskAsync(
            Arg.Any<LoremasterAiRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_WhitespaceOnlyQuestion_DoesNotCallKnowledgeRetriever()
    {
        var command = CreateCommand(question: "     ");

        await _service.AskAsync(command, CancellationToken.None);

        await _knowledgeRetriever.DidNotReceive().RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_WhitespaceOnlyQuestion_DoesNotCallAiClient()
    {
        var command = CreateCommand(question: "\t\n  ");

        await _service.AskAsync(command, CancellationToken.None);

        await _aiClient.DidNotReceive().AskAsync(
            Arg.Any<LoremasterAiRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_QuestionOver2000Characters_DoesNotCallKnowledgeRetriever()
    {
        var longQuestion = new string('X', 2001);
        var command = CreateCommand(question: longQuestion);

        await _service.AskAsync(command, CancellationToken.None);

        await _knowledgeRetriever.DidNotReceive().RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_QuestionOver2000Characters_DoesNotCallAiClient()
    {
        var longQuestion = new string('X', 2001);
        var command = CreateCommand(question: longQuestion);

        await _service.AskAsync(command, CancellationToken.None);

        await _aiClient.DidNotReceive().AskAsync(
            Arg.Any<LoremasterAiRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_QuestionExactly2000Characters_IsValid()
    {
        var exactQuestion = new string('A', 2000);

        _knowledgeRetriever.RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateEmptyContext());

        _aiClient.AskAsync(Arg.Any<LoremasterAiRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreateDefaultAiResponse());

        var command = CreateCommand(question: exactQuestion);

        var result = await _service.AskAsync(command, CancellationToken.None);

        // Should not fail validation — proceeds to retrieval
        await _knowledgeRetriever.Received(1).RetrieveAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CampaignRole>(),
            Arg.Any<CancellationToken>());
    }

    private AskLoremasterCommand CreateCommand(
        string question,
        Guid? campaignId = null,
        Guid? userId = null,
        CampaignRole role = CampaignRole.GM)
    {
        return new AskLoremasterCommand(
            CampaignId: campaignId ?? CampaignId,
            Question: question,
            UserId: userId ?? KeldaUserId,
            UserRole: role,
            ConversationContext: null);
    }

    private static KnowledgeContext CreateEmptyContext() => new()
    {
        Artifacts = new List<KnowledgeArtifact>(),
        Facts = new List<KnowledgeFact>(),
        Relationships = new List<KnowledgeRelationship>(),
        SourceReferences = new List<KnowledgeSourceReference>()
    };

    private static LoremasterAiResponse CreateDefaultAiResponse() => new()
    {
        AnswerText = "I don't have a confirmed source for that yet.",
        InputTokens = 150,
        OutputTokens = 42,
        TotalTokens = 192,
        DurationMs = 620,
        Model = "gpt-4o"
    };
}
