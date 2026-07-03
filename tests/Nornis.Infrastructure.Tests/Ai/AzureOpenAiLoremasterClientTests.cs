using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Infrastructure.Ai;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

[TestFixture]
public class AzureOpenAiLoremasterClientTests
{
    private ChatClient _mockChatClient = null!;
    private IOptions<LoremasterOptions> _options = null!;
    private ILogger<AzureOpenAiLoremasterClient> _logger = null!;
    private AzureOpenAiLoremasterClient _client = null!;

    private static readonly LoremasterAiRequest DefaultRequest = new()
    {
        SystemPrompt = "You are the Loremaster.",
        UserMessage = "Who is Captain Voss?",
        Model = "gpt-4o",
        TimeoutSeconds = 30
    };

    [SetUp]
    public void SetUp()
    {
        _mockChatClient = Substitute.For<ChatClient>();
        _options = Options.Create(new LoremasterOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 30,
            MaxRetrievalCount = 30,
            MaxQuestionLength = 2000
        });
        _logger = NullLogger<AzureOpenAiLoremasterClient>.Instance;
        _client = new AzureOpenAiLoremasterClient(_mockChatClient, _options, _logger);
    }

    #region Helper Methods

    private static ChatCompletion CreateChatCompletion(string answerText)
    {
        var content = new ChatMessageContent(answerText);
        var usage = OpenAIChatModelFactory.ChatTokenUsage(
            outputTokenCount: 200,
            inputTokenCount: 800,
            totalTokenCount: 1000);

        return OpenAIChatModelFactory.ChatCompletion(
            id: "chatcmpl-loremaster-123",
            finishReason: ChatFinishReason.Stop,
            content: content,
            model: "gpt-4o",
            usage: usage);
    }

    private void SetupMockToReturn(string answerText)
    {
        var completion = CreateChatCompletion(answerText);
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(200);
        var result = ClientResult.FromValue(completion, mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
    }

    #endregion

    #region Success Tests

    [Test]
    public async Task AskAsync_ValidResponse_ReturnsLoremasterAiResponse()
    {
        var answerText = "Captain Voss is a shady harbor captain based in Black Harbor. [ref:art-1]";
        SetupMockToReturn(answerText);

        var response = await _client.AskAsync(DefaultRequest, CancellationToken.None);

        Assert.That(response.AnswerText, Is.EqualTo(answerText));
        Assert.That(response.InputTokens, Is.EqualTo(800));
        Assert.That(response.OutputTokens, Is.EqualTo(200));
        Assert.That(response.TotalTokens, Is.EqualTo(1000));
        Assert.That(response.Model, Is.EqualTo("gpt-4o"));
        Assert.That(response.DurationMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task AskAsync_PassesSystemPromptAndUserMessage()
    {
        SetupMockToReturn("Some answer.");

        await _client.AskAsync(DefaultRequest, CancellationToken.None);

        await _mockChatClient.Received(1).CompleteChatAsync(
            Arg.Is<IEnumerable<ChatMessage>>(messages =>
                messages.Count() == 2),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Timeout Tests

    [Test]
    public void AskAsync_Timeout_ThrowsAiLoremasterTimeoutException()
    {
        var shortTimeoutRequest = new LoremasterAiRequest
        {
            SystemPrompt = "System prompt",
            UserMessage = "Question?",
            Model = "gpt-4o",
            TimeoutSeconds = 1
        };

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .Returns<ClientResult<ChatCompletion>>(callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Should not reach here");
            });

        var ex = Assert.ThrowsAsync<AiLoremasterTimeoutException>(
            async () => await _client.AskAsync(shortTimeoutRequest, CancellationToken.None));

        Assert.That(ex!.DurationMs, Is.GreaterThanOrEqualTo(0));
        Assert.That(ex.Message, Does.Contain("timed out"));
    }

    [Test]
    public void AskAsync_ExternalCancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _client.AskAsync(DefaultRequest, cts.Token));
    }

    #endregion

    #region Rate Limit Tests

    [Test]
    public void AskAsync_429Response_ThrowsAiLoremasterRateLimitException()
    {
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(429);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<AiLoremasterRateLimitException>(
            async () => await _client.AskAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.DurationMs, Is.GreaterThanOrEqualTo(0));
        Assert.That(ex.Message, Does.Contain("rate limited"));
    }

    #endregion

    #region Service Error Tests

    [Test]
    public void AskAsync_500Response_ThrowsAiLoremasterServiceException()
    {
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(500);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<AiLoremasterServiceException>(
            async () => await _client.AskAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.HttpStatus, Is.EqualTo(500));
        Assert.That(ex.DurationMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void AskAsync_503Response_ThrowsAiLoremasterServiceException()
    {
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(503);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<AiLoremasterServiceException>(
            async () => await _client.AskAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.HttpStatus, Is.EqualTo(503));
    }

    [Test]
    public void AskAsync_NonServerClientResultException_ThrowsAiLoremasterServiceException()
    {
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(400);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<AiLoremasterServiceException>(
            async () => await _client.AskAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.HttpStatus, Is.EqualTo(400));
    }

    [Test]
    public void AskAsync_UnexpectedException_ThrowsAiLoremasterServiceException()
    {
        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Something unexpected"));

        var ex = Assert.ThrowsAsync<AiLoremasterServiceException>(
            async () => await _client.AskAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.HttpStatus, Is.EqualTo(500));
        Assert.That(ex.Message, Does.Contain("Unexpected error"));
    }

    #endregion

    #region Constructor Validation

    [Test]
    public void Constructor_NullChatClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureOpenAiLoremasterClient(null!, _options, _logger));
    }

    [Test]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureOpenAiLoremasterClient(_mockChatClient, null!, _logger));
    }

    [Test]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureOpenAiLoremasterClient(_mockChatClient, _options, null!));
    }

    #endregion
}
