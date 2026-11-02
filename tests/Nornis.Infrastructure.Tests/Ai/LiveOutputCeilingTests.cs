using System.ClientModel;
using System.Text;
using Microsoft.Extensions.Configuration;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The live half of <see cref="OutputCeilingWireTests"/>: does the deployment accept the ceiling
/// the SDK sends, and honour it? Prep task A2. Explicit, so CI and a plain <c>dotnet test</c>
/// never run it. It makes two small streamed calls to the Loremaster deployment (well under a
/// cent) with the API's own configuration, read from its user secrets or from environment
/// variables (<c>Loremaster__AiEndpoint</c>, <c>Loremaster__AiKey</c>, <c>Loremaster__AiModel</c>).
///
/// Run it by name:
/// <c>dotnet test --project tests/Nornis.Infrastructure.Tests --filter "FullyQualifiedName~LiveOutputCeilingTests" --output detailed</c>
/// and record what it prints in docs/features/29-prep/tasks.md.
/// </summary>
[TestFixture]
[Explicit("Calls the production Azure OpenAI deployment")]
[Category("Live")]
public class LiveOutputCeilingTests
{
    // Nornis.Api's UserSecretsId: the same configuration the API reads in development.
    private const string ApiUserSecretsId = "4ebe1683-4c09-44dd-a16c-d5e94091179d";

    [Test]
    public async Task TheDeploymentAcceptsACeiling_AndAnswers()
    {
        var result = await StreamAsync("In one sentence, describe a harbor town at dawn.", ceiling: 2500);

        TestContext.Out.WriteLine(result.Describe("ceiling 2500"));
        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Is.Not.Empty, "the deployment accepted the ceiling but returned no text");
            Assert.That(result.FinishReason, Is.EqualTo(ChatFinishReason.Stop));
        });
    }

    [Test]
    public async Task TheDeploymentHonoursTheCeiling_WhenTheAnswerWouldRunPastIt()
    {
        var result = await StreamAsync("Write a 400-word history of a harbor town.", ceiling: 64);

        TestContext.Out.WriteLine(result.Describe("ceiling 64"));
        Assert.Multiple(() =>
        {
            Assert.That(result.FinishReason, Is.EqualTo(ChatFinishReason.Length),
                "a ceiling the model ignores is no ceiling");
            Assert.That(result.OutputTokens, Is.LessThanOrEqualTo(64));
        });
    }

    private static async Task<LiveResult> StreamAsync(string prompt, int ceiling)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var endpoint = configuration["Loremaster:AiEndpoint"];
        var key = configuration["Loremaster:AiKey"];
        var deployment = configuration["Loremaster:AiModel"] ?? "nornis-ask";
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            Assert.Ignore("Loremaster:AiEndpoint and Loremaster:AiKey are not configured on this machine.");
        }

        // The same factory and deployment the API builds the Loremaster's client from.
        var chat = AzureOpenAiClientFactory
            .Create(new Uri(endpoint!), new ApiKeyCredential(key!), maxRetries: 0)
            .GetChatClient(deployment);

        var text = new StringBuilder();
        var result = new LiveResult { Deployment = deployment };
        await foreach (var update in chat.CompleteChatStreamingAsync(
            [new UserChatMessage(prompt)],
            new ChatCompletionOptions { MaxOutputTokenCount = ceiling }))
        {
            foreach (var part in update.ContentUpdate)
                text.Append(part.Text);
            if (update.FinishReason is { } reason)
                result.FinishReason = reason;
            if (update.Usage is { } usage)
            {
                result.OutputTokens = usage.OutputTokenCount;
                result.ReasoningTokens = usage.OutputTokenDetails?.ReasoningTokenCount;
            }
            if (!string.IsNullOrEmpty(update.Model))
                result.Model = update.Model;
        }

        result.Text = text.ToString();
        return result;
    }

    private sealed class LiveResult
    {
        public string Deployment { get; init; } = "";
        public string? Model { get; set; }
        public string Text { get; set; } = "";
        public ChatFinishReason? FinishReason { get; set; }
        public int? OutputTokens { get; set; }
        public int? ReasoningTokens { get; set; }

        public string Describe(string label) =>
            $"{label}: deployment {Deployment}, model {Model}, finish {FinishReason}, "
            + $"output tokens {OutputTokens}, of which reasoning {ReasoningTokens}, text {Text.Length} chars";
    }
}
