using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Infrastructure.Ai;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

[TestFixture]
public class AzureOpenAiExtractionClientTests
{
    private ChatClient _mockChatClient = null!;
    private ILogger<AzureOpenAiExtractionClient> _logger = null!;
    private AzureOpenAiExtractionClient _client = null!;

    // The client receives finished prompt strings; what those strings say is
    // ExtractionPromptBuilder's contract, tested in Application.Tests.
    private static readonly AiPromptRequest DefaultRequest = new()
    {
        SystemPrompt = "You are the extraction engine for a test.",
        UserMessage = "We questioned Captain Voss in Black Harbor.",
        Model = "gpt-4o",
        TimeoutSeconds = 60
    };

    [SetUp]
    public void SetUp()
    {
        _mockChatClient = Substitute.For<ChatClient>();
        _logger = NullLogger<AzureOpenAiExtractionClient>.Instance;
        _client = new AzureOpenAiExtractionClient(_mockChatClient, _logger);
    }

    #region Helper Methods

    private static ChatCompletion CreateChatCompletion(string responseJson)
    {
        var content = new ChatMessageContent(responseJson);
        var usage = OpenAIChatModelFactory.ChatTokenUsage(
            outputTokenCount: 150,
            inputTokenCount: 500,
            totalTokenCount: 650);

        return OpenAIChatModelFactory.ChatCompletion(
            id: "chatcmpl-test-123",
            finishReason: ChatFinishReason.Stop,
            content: content,
            model: "gpt-4o",
            usage: usage);
    }

    private void SetupMockToReturn(string responseJson)
    {
        var completion = CreateChatCompletion(responseJson);
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(200);
        var result = ClientResult.FromValue(completion, mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
    }

    /// <summary>
    /// A proposal the client accepts. The parse-failure tests below each break exactly
    /// one field of it, so what a case asserts is legible from the field name alone.
    /// </summary>
    private const string ValidProposal = """
        {
          "changeType": "CreateArtifact",
          "targetType": "Artifact",
          "targetId": null,
          "proposedValue": { "name": "Voss" },
          "rationale": "Test rationale.",
          "confidence": 0.8
        }
        """;

    /// <summary>
    /// The valid proposal with one field dropped, wrapped in the response envelope.
    /// </summary>
    private static string ResponseWithout(string field)
    {
        var proposal = JsonNode.Parse(ValidProposal)!.AsObject();
        proposal.Remove(field);
        return Envelope(proposal);
    }

    /// <summary>
    /// The valid proposal with one field replaced by <paramref name="rawJson"/> — a JSON
    /// literal, so a case can supply a string, a number or a wrong-typed value alike.
    /// </summary>
    private static string ResponseWith(string field, string rawJson)
    {
        var proposal = JsonNode.Parse(ValidProposal)!.AsObject();
        proposal[field] = JsonNode.Parse(rawJson);
        return Envelope(proposal);
    }

    private static string Envelope(JsonObject proposal) =>
        new JsonObject { ["proposals"] = new JsonArray(proposal) }.ToJsonString();

    #endregion

    #region Valid Response Tests

    [Test]
    public async Task ExtractAsync_ValidResponse_ReturnsCorrectProposals()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Captain Voss", "type": "Character", "visibility": "PartyVisible" },
                  "rationale": "New character mentioned in session notes.",
                  "confidence": 0.85
                },
                {
                  "changeType": "AddFact",
                  "targetType": "ArtifactFact",
                  "targetId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                  "proposedValue": { "predicate": "location", "value": "Black Harbor", "visibility": "PartyVisible" },
                  "rationale": "Captain Voss was encountered in Black Harbor.",
                  "confidence": 0.92
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        var result = await _client.ExtractAsync(DefaultRequest, CancellationToken.None);

        Assert.That(result.Proposals, Has.Count.EqualTo(2));

        var first = result.Proposals[0];
        Assert.That(first.ChangeType, Is.EqualTo("CreateArtifact"));
        Assert.That(first.TargetType, Is.EqualTo("Artifact"));
        Assert.That(first.TargetId, Is.Null);
        Assert.That(first.Rationale, Is.EqualTo("New character mentioned in session notes."));
        Assert.That(first.Confidence, Is.EqualTo(0.85m));
        Assert.That(first.ProposedValue, Is.Not.Null);

        var second = result.Proposals[1];
        Assert.That(second.ChangeType, Is.EqualTo("AddFact"));
        Assert.That(second.TargetType, Is.EqualTo("ArtifactFact"));
        Assert.That(second.TargetId, Is.EqualTo(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")));
        Assert.That(second.Rationale, Is.EqualTo("Captain Voss was encountered in Black Harbor."));
        Assert.That(second.Confidence, Is.EqualTo(0.92m));
    }

    [Test]
    public async Task ExtractAsync_ValidResponse_ReturnsCorrectTokenUsage()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Silver Key", "visibility": "PartyVisible" },
                  "rationale": "New item discovered in the session.",
                  "confidence": 0.9
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        var result = await _client.ExtractAsync(DefaultRequest, CancellationToken.None);

        Assert.That(result.Usage.InputTokens, Is.EqualTo(500));
        Assert.That(result.Usage.OutputTokens, Is.EqualTo(150));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(650));
        Assert.That(result.Usage.Model, Is.EqualTo("gpt-4o"));
        Assert.That(result.Usage.DurationMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task ExtractAsync_EmptyProposalsArray_ReturnsSuccessWithEmptyList()
    {
        var responseJson = """
            {
              "proposals": []
            }
            """;

        SetupMockToReturn(responseJson);

        var result = await _client.ExtractAsync(DefaultRequest, CancellationToken.None);

        Assert.That(result.Proposals, Is.Empty);
        Assert.That(result.Usage.InputTokens, Is.EqualTo(500));
    }

    #endregion

    #region Parse Failure Tests

    [TestCase("changeType")]
    [TestCase("targetType")]
    [TestCase("rationale")]
    [TestCase("confidence")]
    [TestCase("proposedValue")]
    public void ExtractAsync_ProposalMissingARequiredField_ThrowsParseExceptionNamingIt(string field)
    {
        SetupMockToReturn(ResponseWithout(field));

        var ex = Assert.ThrowsAsync<AiParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        // The message reaches the retry loop's log, and that log line is all anyone has
        // when a model starts omitting a field — so naming the field is the contract.
        Assert.That(ex!.Message, Does.Contain(field));
    }

    [TestCase("changeType", "\"DeleteArtifact\"", Description = "not one of the four verbs")]
    [TestCase("targetType", "\"World\"", Description = "not a proposable entity")]
    [TestCase("confidence", "1.5", Description = "above the 0.0-1.0 range")]
    [TestCase("confidence", "-0.1", Description = "below the 0.0-1.0 range")]
    [TestCase("rationale", "\"\"", Description = "present but empty")]
    public void ExtractAsync_ProposalFieldOutOfRange_ThrowsParseExceptionNamingIt(
        string field, string rawJson)
    {
        SetupMockToReturn(ResponseWith(field, rawJson));

        var ex = Assert.ThrowsAsync<AiParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain(field));
    }

    [Test]
    public void ExtractAsync_RationaleExceeding500Chars_ThrowsParseExceptionNamingIt()
    {
        // Separate from the range cases above only because a 501-character literal is not
        // a compile-time constant and so cannot ride in a [TestCase].
        SetupMockToReturn(ResponseWith("rationale", $"\"{new string('x', 501)}\""));

        var ex = Assert.ThrowsAsync<AiParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("rationale"));
    }

    [Test]
    public async Task ExtractAsync_MoreThan50Proposals_ClampsToFirst50()
    {
        var proposals = string.Join(",\n", Enumerable.Range(0, 54).Select(i => $$"""
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Artifact{{i}}" },
                  "rationale": "Proposal number {{i}}.",
                  "confidence": 0.5
                }
            """));

        var responseJson = $$"""
            {
              "proposals": [
                {{proposals}}
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        var response = await _client.ExtractAsync(DefaultRequest, CancellationToken.None);

        Assert.That(response.Proposals, Has.Count.EqualTo(50));
        // The first 50 in response order survive; the tail is dropped.
        Assert.That(response.Proposals[0].Rationale, Is.EqualTo("Proposal number 0."));
        Assert.That(response.Proposals[49].Rationale, Is.EqualTo("Proposal number 49."));
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void ExtractAsync_Timeout_ThrowsAiTimeoutException()
    {
        // The timeout rides on the request now, not on options.
        var shortTimeoutRequest = new AiPromptRequest
        {
            SystemPrompt = DefaultRequest.SystemPrompt,
            UserMessage = DefaultRequest.UserMessage,
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
                // Simulate a long-running operation that will be cancelled by the timeout
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Should not reach here");
            });

        Assert.ThrowsAsync<AiTimeoutException>(
            async () => await _client.ExtractAsync(shortTimeoutRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_429Response_ThrowsAiHttpException()
    {
        var exception = new ClientResultException(
            "Too Many Requests",
            Substitute.For<PipelineResponse>());

        // Use reflection to set the Status property since it's read from the response
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(429);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<AiHttpException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(429));
    }

    [Test]
    public void ExtractAsync_503Response_ThrowsAiHttpException()
    {
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(503);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<AiHttpException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public void ExtractAsync_NetworkException_ThrowsAiHttpException()
    {
        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        Assert.ThrowsAsync<AiHttpException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    #endregion
}
