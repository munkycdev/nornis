using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
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
public class AzureOpenAiExtractionClientTests
{
    private ChatClient _mockChatClient = null!;
    private IOptions<ExtractionOptions> _options = null!;
    private ILogger<AzureOpenAiExtractionClient> _logger = null!;
    private AzureOpenAiExtractionClient _client = null!;

    private static readonly ExtractionRequest DefaultRequest = new()
    {
        SourceBody = "We questioned Captain Voss in Black Harbor.",
        SourceTitle = "Session 5 Notes",
        SourceType = "SessionNote",
        SourceVisibility = "PartyVisible"
    };

    [SetUp]
    public void SetUp()
    {
        _mockChatClient = Substitute.For<ChatClient>();
        _options = Options.Create(new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 60,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2
        });
        _logger = NullLogger<AzureOpenAiExtractionClient>.Instance;
        _client = new AzureOpenAiExtractionClient(_mockChatClient, _options, _logger);
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

        Assert.That(result.InputTokens, Is.EqualTo(500));
        Assert.That(result.OutputTokens, Is.EqualTo(150));
        Assert.That(result.TotalTokens, Is.EqualTo(650));
        Assert.That(result.Model, Is.EqualTo("gpt-4o"));
        Assert.That(result.DurationMs, Is.GreaterThanOrEqualTo(0));
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
        Assert.That(result.InputTokens, Is.EqualTo(500));
    }

    #endregion

    #region Parse Failure Tests

    [Test]
    public void ExtractAsync_MissingChangeTypeField_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale.",
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_MissingTargetTypeField_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale.",
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_MissingRationaleField_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_MissingConfidenceField_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale."
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_MissingProposedValueField_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "rationale": "Test rationale.",
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_InvalidChangeType_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "DeleteArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale.",
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_InvalidTargetType_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "World",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale.",
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_RationaleExceeding500Chars_ThrowsParseException()
    {
        var longRationale = new string('x', 501);
        var responseJson = $$"""
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "{{longRationale}}",
                  "confidence": 0.8
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_ConfidenceAbove1_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale.",
                  "confidence": 1.5
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_ConfidenceBelowZero_ThrowsParseException()
    {
        var responseJson = """
            {
              "proposals": [
                {
                  "changeType": "CreateArtifact",
                  "targetType": "Artifact",
                  "targetId": null,
                  "proposedValue": { "name": "Voss" },
                  "rationale": "Test rationale.",
                  "confidence": -0.1
                }
              ]
            }
            """;

        SetupMockToReturn(responseJson);

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_MoreThan50Proposals_ThrowsParseException()
    {
        var proposals = string.Join(",\n", Enumerable.Range(0, 51).Select(i => $$"""
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

        Assert.ThrowsAsync<AiExtractionParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void ExtractAsync_Timeout_ThrowsAiExtractionTimeoutException()
    {
        // Configure a very short timeout
        var shortTimeoutOptions = Options.Create(new ExtractionOptions
        {
            AiModel = "gpt-4o",
            AiEndpoint = "https://test.openai.azure.com/",
            AiTimeoutSeconds = 1,
            MaxArtifactContextCount = 50,
            MaxFactsPerArtifact = 20,
            MaxParseRetryAttempts = 2
        });

        var client = new AzureOpenAiExtractionClient(_mockChatClient, shortTimeoutOptions, _logger);

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

        Assert.ThrowsAsync<AiExtractionTimeoutException>(
            async () => await client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    [Test]
    public void ExtractAsync_429Response_ThrowsHttpRequestException()
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

        var ex = Assert.ThrowsAsync<HttpRequestException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public void ExtractAsync_503Response_ThrowsHttpRequestException()
    {
        var mockResponse = Substitute.For<PipelineResponse>();
        mockResponse.Status.Returns(503);
        var clientException = new ClientResultException(mockResponse);

        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(clientException);

        var ex = Assert.ThrowsAsync<HttpRequestException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public void ExtractAsync_NetworkException_ThrowsHttpRequestException()
    {
        _mockChatClient.CompleteChatAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatCompletionOptions>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        Assert.ThrowsAsync<HttpRequestException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    #endregion

    #region System Prompt Tests

    [Test]
    public void BuildSystemPrompt_IncludesVisibilityInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "GMOnly"
        };

        var prompt = AzureOpenAiExtractionClient.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("GMOnly"));
        Assert.That(prompt, Does.Contain("visibility"));
        Assert.That(prompt, Does.Contain("Never produce a proposal with visibility broader than its source"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesTruthStateInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = AzureOpenAiExtractionClient.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Confirmed"));
        Assert.That(prompt, Does.Contain("Likely"));
        Assert.That(prompt, Does.Contain("Rumor"));
        Assert.That(prompt, Does.Contain("Disputed"));
        Assert.That(prompt, Does.Contain("Hidden"));
        Assert.That(prompt, Does.Contain("Truth State"));
    }

    [Test]
    public void BuildSystemPrompt_PrivateSource_InstructsPrivateVisibility()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "JournalEntry",
            SourceVisibility = "Private"
        };

        var prompt = AzureOpenAiExtractionClient.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Private"));
        Assert.That(prompt, Does.Contain("MUST include \"visibility\": \"Private\""));
    }

    [Test]
    public void BuildSystemPrompt_IncludesLiterarySourceInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = AzureOpenAiExtractionClient.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("Literary and Authored Sources"));
        Assert.That(prompt, Does.Contain("Document artifact for the work itself"));
        Assert.That(prompt, Does.Contain("at best Likely, never Confirmed"));
        Assert.That(prompt, Does.Contain("Still extract the real artifacts the work establishes"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesRationaleInstructions()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = AzureOpenAiExtractionClient.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("rationale"));
        Assert.That(prompt, Does.Contain("max 500 characters"));
    }

    [Test]
    public void BuildSystemPrompt_IncludesOpenQuestionConvention()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Test body",
            SourceTitle = "Test",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible"
        };

        var prompt = AzureOpenAiExtractionClient.BuildSystemPrompt(request);

        Assert.That(prompt, Does.Contain("open question"));
        Assert.That(prompt, Does.Contain("re-propose an open question that already exists"));
    }

    #endregion

    #region BuildUserMessage Tests

    [Test]
    public void BuildUserMessage_IncludesSourceFields()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Captain Voss was seen at the docks.",
            SourceTitle = "Session 7 Notes",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            OccurredAt = new DateTimeOffset(2024, 3, 15, 20, 0, 0, TimeSpan.Zero)
        };

        var message = AzureOpenAiExtractionClient.BuildUserMessage(request);

        Assert.That(message, Does.Contain("Session 7 Notes"));
        Assert.That(message, Does.Contain("SessionNote"));
        Assert.That(message, Does.Contain("PartyVisible"));
        Assert.That(message, Does.Contain("Captain Voss was seen at the docks."));
        Assert.That(message, Does.Contain("2024-03-15"));
    }

    [Test]
    public void BuildUserMessage_NullOccurredAt_OmitsTemporalContext()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Some content",
            SourceTitle = "Test Note",
            SourceType = "GMNote",
            SourceVisibility = "GMOnly",
            OccurredAt = null
        };

        var message = AzureOpenAiExtractionClient.BuildUserMessage(request);

        Assert.That(message, Does.Not.Contain("Occurred At"));
    }

    [Test]
    public void BuildUserMessage_WithExistingArtifacts_IncludesArtifactContext()
    {
        var request = new ExtractionRequest
        {
            SourceBody = "Some content",
            SourceTitle = "Test Note",
            SourceType = "SessionNote",
            SourceVisibility = "PartyVisible",
            ExistingArtifacts =
            [
                new ArtifactContext
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Captain Voss",
                    Type = "Character",
                    Summary = "A shady harbor captain.",
                    Facts =
                    [
                        new FactContext { Predicate = "location", Value = "Black Harbor" },
                        new FactContext { Predicate = "occupation", Value = "Ship captain" }
                    ]
                }
            ]
        };

        var message = AzureOpenAiExtractionClient.BuildUserMessage(request);

        Assert.That(message, Does.Contain("Captain Voss"));
        Assert.That(message, Does.Contain("11111111-1111-1111-1111-111111111111"));
        Assert.That(message, Does.Contain("Character"));
        Assert.That(message, Does.Contain("A shady harbor captain."));
        Assert.That(message, Does.Contain("location: Black Harbor"));
        Assert.That(message, Does.Contain("occupation: Ship captain"));
    }

    #endregion
}
