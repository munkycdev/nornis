using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

[TestFixture]
public class ExtractionAiClientTests
{
    private FakeChatClient _chatClient = null!;
    private ILogger<ExtractionAiClient> _logger = null!;
    private ExtractionAiClient _client = null!;

    // The client receives finished prompt strings; what those strings say is
    // ExtractionPromptBuilder's contract, tested in Application.Tests.
    private static readonly AiPromptRequest DefaultRequest = new()
    {
        Scope = AiCallScope.ForWorld(Guid.NewGuid()),
        SystemPrompt = "You are the extraction engine for a test.",
        UserMessage = "We questioned Captain Voss in Black Harbor.",
        Model = "gpt-4o",
        TimeoutSeconds = 60
    };

    [SetUp]
    public void SetUp()
    {
        _chatClient = new FakeChatClient();
        _logger = NullLogger<ExtractionAiClient>.Instance;
        _client = new ExtractionAiClient(new FakeAiCallBinder(_chatClient), _logger);
    }

    #region Helper Methods

    private void SetupMockToReturn(string responseJson) =>
        _chatClient.Returns(responseJson, input: 500, output: 150, total: 650);

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

    #region Significance (feature 43)

    [TestCase("Core", ProposalSignificance.Core)]
    [TestCase("Supporting", ProposalSignificance.Supporting)]
    [TestCase("Incidental", ProposalSignificance.Incidental)]
    public async Task ExtractAsync_Significance_IsParsed(string sent, ProposalSignificance expected)
    {
        SetupMockToReturn(ResponseWith("significance", $"\"{sent}\""));

        var result = await _client.ExtractAsync(DefaultRequest, CancellationToken.None);

        Assert.That(result.Proposals.Single().Significance, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExtractAsync_SignificanceAbsentOrNull_IsSupporting()
    {
        // A model that did not say has not called it incidental; a non-strict endpoint that
        // ignores the schema must not lose every proposal to triage.
        SetupMockToReturn(ResponseWithout("significance"));
        var absent = (await _client.ExtractAsync(DefaultRequest, CancellationToken.None)).Proposals.Single();

        SetupMockToReturn(ResponseWith("significance", "null"));
        var sentNull = (await _client.ExtractAsync(DefaultRequest, CancellationToken.None)).Proposals.Single();

        Assert.Multiple(() =>
        {
            Assert.That(absent.Significance, Is.EqualTo(ProposalSignificance.Supporting));
            Assert.That(sentNull.Significance, Is.EqualTo(ProposalSignificance.Supporting));
        });
    }

    [TestCase("\"Trivial\"", Description = "not one of the three")]
    [TestCase("\"core\"", Description = "the schema's enum is case-sensitive")]
    [TestCase("\"1\"", Description = "a numeric string Enum.TryParse would have taken")]
    [TestCase("2", Description = "a number")]
    public async Task ExtractAsync_SignificanceOutsideTheEnum_ThrowsParseExceptionNamingIt(string rawJson)
    {
        SetupMockToReturn(ResponseWith("significance", rawJson));

        // NUnit 5: ThrowsAsync returns a Task and must be awaited, or it silently never asserts.
        var ex = await Assert.ThrowsAsync<AiParseException>(() => _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("significance"));
    }

    [Test]
    public void StructuredOutputSchema_RequiresSignificance_AtTheProposalLevel_NotInThePayload()
    {
        var schema = JsonNode.Parse(ExtractionAiClient.GetStructuredOutputSchema())!;
        var proposal = schema["properties"]!["proposals"]!["items"]!;
        var payload = proposal["properties"]!["proposedValue"]!;

        Assert.Multiple(() =>
        {
            Assert.That(proposal["properties"]!["significance"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()),
                Is.EquivalentTo(["Core", "Supporting", "Incidental"]));
            Assert.That(proposal["required"]!.AsArray().Select(n => n!.GetValue<string>()), Does.Contain("significance"));
            Assert.That(payload["properties"]!["significance"], Is.Null,
                "significance describes the proposal, not the thing proposed; the payload's sixteen fields are counted by the Anthropic fit");
        });
    }

    #endregion

    #region Valid Response Tests

    [Test]
    public async Task ExtractAsync_StrictPayloadWithNulls_ArrivesAsTheOldOpenPayload()
    {
        // Feature 39: the strict schema makes the model send every payload field, nulls included.
        // Downstream code reads the payload by field presence, so the nulls must not survive.
        SetupMockToReturn(ResponseWith("proposedValue", """
            { "name": "Voss", "type": "Character", "summary": null, "predicate": null, "confidence": null }
            """));

        var result = await _client.ExtractAsync(DefaultRequest, CancellationToken.None);

        var payload = JsonSerializer.Serialize(result.Proposals.Single().ProposedValue);
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(payload), JsonNode.Parse("""{"name":"Voss","type":"Character"}""")),
            Is.True, payload);
    }

    [Test]
    public async Task ExtractAsync_FieldsLeftOutInsteadOfNull_ParseAsTheNullsWould()
    {
        // On a Claude key the schema's nullable fields arrive as optional ones
        // (AnthropicSchemaLimits), so the model leaves a field out where it would send null.
        // Both shapes must give the same proposal.
        const string withNulls = """
            {"proposals":[{"changeType":"CreateArtifact","targetType":"Artifact","targetId":null,
              "proposedValue":{"name":"Voss","type":"Character","summary":null,"confidence":null},
              "rationale":"Named in the session.","confidence":0.8,"quote":null}]}
            """;
        const string leftOut = """
            {"proposals":[{"changeType":"CreateArtifact","targetType":"Artifact",
              "proposedValue":{"name":"Voss","type":"Character"},
              "rationale":"Named in the session.","confidence":0.8}]}
            """;

        SetupMockToReturn(withNulls);
        var fromNulls = (await _client.ExtractAsync(DefaultRequest, CancellationToken.None)).Proposals.Single();
        SetupMockToReturn(leftOut);
        var fromLeftOut = (await _client.ExtractAsync(DefaultRequest, CancellationToken.None)).Proposals.Single();

        Assert.Multiple(() =>
        {
            Assert.That(fromLeftOut.TargetId, Is.Null);
            Assert.That(fromLeftOut.Quote, Is.Null);
            Assert.That(JsonSerializer.Serialize(fromLeftOut.ProposedValue), Is.EqualTo(JsonSerializer.Serialize(fromNulls.ProposedValue)));
            Assert.That(fromNulls.TargetId, Is.Null);
            Assert.That(fromNulls.Quote, Is.Null);
        });
    }

    [Test]
    public void WithoutNulls_KeepsFalseZeroAndEmpty_DropsOnlyNull()
    {
        var payload = JsonNode.Parse("""{"a":null,"b":0,"c":false,"d":"","e":{"f":null}}""")!;

        var kept = ExtractionAiClient.WithoutNulls(payload);

        // Only the top level: nested objects aren't part of the payload grammar, so they pass as sent.
        Assert.That(kept.ToJsonString(), Is.EqualTo("""{"b":0,"c":false,"d":"","e":{"f":null}}"""));
    }

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
    public async Task ExtractAsync_ProposalMissingARequiredField_ThrowsParseExceptionNamingIt(string field)
    {
        SetupMockToReturn(ResponseWithout(field));

        var ex = await Assert.ThrowsAsync<AiParseException>(
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
    public async Task ExtractAsync_ProposalFieldOutOfRange_ThrowsParseExceptionNamingIt(
        string field, string rawJson)
    {
        SetupMockToReturn(ResponseWith(field, rawJson));

        var ex = await Assert.ThrowsAsync<AiParseException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain(field));
    }

    [Test]
    public async Task ExtractAsync_RationaleExceeding500Chars_ThrowsParseExceptionNamingIt()
    {
        // Separate from the range cases above only because a 501-character literal is not
        // a compile-time constant and so cannot ride in a [TestCase].
        SetupMockToReturn(ResponseWith("rationale", $"\"{new string('x', 501)}\""));

        var ex = await Assert.ThrowsAsync<AiParseException>(
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
    public async Task ExtractAsync_Timeout_ThrowsAiTimeoutException()
    {
        // The timeout rides on the request now, not on options.
        var shortTimeoutRequest = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(Guid.NewGuid()),
            SystemPrompt = DefaultRequest.SystemPrompt,
            UserMessage = DefaultRequest.UserMessage,
            Model = "gpt-4o",
            TimeoutSeconds = 1
        };

        _chatClient.Hangs();

        await Assert.ThrowsAsync<AiTimeoutException>(
            async () => await _client.ExtractAsync(shortTimeoutRequest, CancellationToken.None));
    }

    [Test]
    public async Task ExtractAsync_429Response_ThrowsAiHttpException()
    {
        _chatClient.Throws(FakeChatClient.HttpFailure(429));

        var ex = await Assert.ThrowsAsync<AiHttpException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(429));
    }

    [Test]
    public async Task ExtractAsync_503Response_ThrowsAiHttpException()
    {
        _chatClient.Throws(FakeChatClient.HttpFailure(503));

        var ex = await Assert.ThrowsAsync<AiHttpException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public async Task ExtractAsync_NetworkException_ThrowsAiHttpException()
    {
        _chatClient.Throws(new HttpRequestException("Network error"));

        await Assert.ThrowsAsync<AiHttpException>(
            async () => await _client.ExtractAsync(DefaultRequest, CancellationToken.None));
    }

    #endregion
}
