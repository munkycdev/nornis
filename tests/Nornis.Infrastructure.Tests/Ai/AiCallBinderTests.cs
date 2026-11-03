using System.ClientModel;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI;

namespace Nornis.Infrastructure.Tests.Ai;

[TestFixture]
public class AiCallBinderTests
{
    private static readonly NornisChatClients Nornis = NornisChatClients.Create(
        new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://example.openai.azure.com/openai/v1/"),
        }));

    [Test]
    public async Task NornisFunding_BindsNornisClientForThatDeployment_OnTheOpenAiWire()
    {
        var binder = new AiCallBinder(new FixedFunding(AiProvider.AzureOpenAi, "nornis-ask"), Nornis);

        var binding = await binder.BindAsync(AiCallScope.ForWorld(Guid.NewGuid()), AiTask.Ask, "nornis-ask", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(binding.Client, Is.SameAs(Nornis.For("nornis-ask")), "one client per deployment, kept");
            Assert.That(binding.Client, Is.Not.SameAs(Nornis.For("nornis-extract")));
            Assert.That(binding.Traits, Is.SameAs(OpenAiProviderTraits.Instance));
            Assert.That(binding.Funding.FundedBy, Is.EqualTo(AiFundedBy.Nornis));
        });
    }

    [TestCase(AiProvider.OpenAi)]
    [TestCase(AiProvider.Anthropic)]
    [TestCase(AiProvider.Gemini)]
    [TestCase(AiProvider.OpenAiCompatible)]
    public void UserKeyProviders_AreNotBuiltYet(AiProvider provider)
    {
        // Phase A ships the seam only; a funding answer naming a user's provider is a bug until Phase B.
        var binder = new AiCallBinder(new FixedFunding(provider, "some-model"), Nornis);

        Assert.ThrowsAsync<NotSupportedException>(() =>
            binder.BindAsync(AiCallScope.ForWorld(Guid.NewGuid()), AiTask.Ask, "nornis-ask", CancellationToken.None));
    }

    private sealed class FixedFunding(AiProvider provider, string model) : IAiFundingService
    {
        public Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct) =>
            Task.FromResult(new AiFunding(provider, model, provider == AiProvider.AzureOpenAi ? AiFundedBy.Nornis : AiFundedBy.Key));
    }
}
