using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

[TestFixture]
public class AzureOpenAiClientFactoryTests
{
    /// <summary>
    /// Configuration holds the resource root, written by hand in either form. The v1 path is the
    /// factory's to add, and a missing or doubled slash would send every call to a 404.
    /// </summary>
    [TestCase("https://x.openai.azure.com/")]
    [TestCase("https://x.openai.azure.com")]
    public void V1Endpoint_AppendsTheV1PathToTheResourceRoot(string configured)
    {
        var endpoint = AzureOpenAiClientFactory.V1Endpoint(new Uri(configured));

        Assert.That(endpoint.AbsoluteUri, Is.EqualTo("https://x.openai.azure.com/openai/v1/"));
    }

    /// <summary>
    /// The executor owns every AI call's timeout. If the SDK keeps its own 100-second network
    /// timeout, an extraction configured for 180 seconds is cancelled at 100 with a bare "AI call
    /// timed out." and requeued, paying again each time — which is what happened on 2026-11-06.
    /// Both builders are checked, Nornis's own client and a user's OpenAI key, because a user's
    /// key once built its options by hand and missed what Nornis's had.
    /// </summary>
    [Test]
    public void OpenAiClients_LeaveTheTimeoutToTheExecutor()
    {
        using var userKeys = new UserKeyChatClients(maxRetries: 0);

        Assert.Multiple(() =>
        {
            Assert.That(AzureOpenAiClientFactory.Options(0).NetworkTimeout, Is.EqualTo(Timeout.InfiniteTimeSpan), "Nornis's own client");
            Assert.That(userKeys.OpenAiOptions().NetworkTimeout, Is.EqualTo(Timeout.InfiniteTimeSpan), "a user's OpenAI key");
        });
    }
}
