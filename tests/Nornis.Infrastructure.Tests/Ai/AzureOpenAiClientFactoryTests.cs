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
}
