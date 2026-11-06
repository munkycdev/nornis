using System.ClientModel;
using Microsoft.Extensions.AI;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
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

    private InMemoryAiKeyRepository _keys = null!;
    private AiOutcomeMonitor _outcomes = null!;
    private AiKeyProtector _protector = null!;
    private UserKeyChatClients _userKeys = null!;

    [SetUp]
    public void SetUp()
    {
        _keys = new InMemoryAiKeyRepository();
        _outcomes = new AiOutcomeMonitor();
        _protector = AiKeyProtector.Create(keyBlobUri: null);
        _userKeys = new UserKeyChatClients(maxRetries: 0);
    }

    [TearDown]
    public void TearDown() => _userKeys.Dispose();

    private AiCallBinder Binder(Func<AiFunding> funding) =>
        new(new FixedFunding(funding), Nornis, _userKeys, _keys, _protector, _outcomes, Microsoft.Extensions.Options.Options.Create(new AiModelsOptions()), TimeProvider.System);

    private UserAiKey SavedKey(AiProvider provider = AiProvider.OpenAi, string? baseUrl = null)
    {
        var key = _keys.Add(Guid.NewGuid(), provider);
        key.ProtectedKey = _protector.Protect("sk-test-1234");
        key.BaseUrl = baseUrl;
        return key;
    }

    private static AiFunding KeyFunding(UserAiKey key, string model = "gpt-5.4") =>
        new(key.Provider, model, AiFundedBy.Key, key.Id, key.UserId);

    private static Task<AiBinding> Bind(AiCallBinder binder, bool needsWebSearch = false) =>
        binder.BindAsync(AiCallScope.ForWorld(Guid.NewGuid()), AiTask.Prep, "nornis-ask", needsImages: false, needsWebSearch, CancellationToken.None);

    [Test]
    public async Task NornisFunding_BindsNornisClientForThatDeployment_OnTheOpenAiWire()
    {
        var binding = await Bind(Binder(() => new AiFunding(AiProvider.AzureOpenAi, "nornis-ask", AiFundedBy.Nornis)));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Client, Is.SameAs(Nornis.For("nornis-ask")), "one client per deployment, kept");
            Assert.That(binding.Client, Is.Not.SameAs(Nornis.For("nornis-extract")));
            Assert.That(binding.Traits, Is.SameAs(OpenAiProviderTraits.Instance));
            Assert.That(binding.KeyVersion, Is.Null);
        });
    }

    [Test]
    public async Task TrialFunding_RunsOnNornisClient()
    {
        var binding = await Bind(Binder(() => new AiFunding(AiProvider.AzureOpenAi, "nornis-ask", AiFundedBy.Trial)));

        Assert.That(binding.Client, Is.SameAs(Nornis.For("nornis-ask")));
    }

    [TestCase(AiProvider.OpenAi, null)]
    [TestCase(AiProvider.OpenAiCompatible, "https://models.example.com/v1/")]
    public async Task KeyFunding_BuildsOneClientPerKeyVersion_AndKeepsIt(AiProvider provider, string? baseUrl)
    {
        var key = SavedKey(provider, baseUrl);
        var binder = Binder(() => KeyFunding(key));

        var first = await Bind(binder);
        var second = await Bind(binder);
        key.UpdatedAt = key.UpdatedAt.AddMinutes(1);
        var replaced = await Bind(binder);

        Assert.Multiple(() =>
        {
            Assert.That(first.Client, Is.Not.SameAs(Nornis.For("gpt-5.4")));
            Assert.That(second.Client, Is.SameAs(first.Client), "cached while the key is unchanged");
            Assert.That(replaced.Client, Is.Not.SameAs(first.Client), "a replaced key is never served from the cache");
            Assert.That(first.KeyVersion, Is.EqualTo(DateTimeOffset.UnixEpoch));
        });
    }

    [Test]
    public async Task KeyFunding_UnreadableCiphertext_IsARejectedKey_AndMarksIt()
    {
        // What a lost Data Protection ring looks like: the ciphertext no longer opens.
        var key = SavedKey();
        key.ProtectedKey = AiKeyProtector.Create(null).Protect("sk-from-another-ring");

        var ex = await Assert.ThrowsAsync<AiKeyRejectedException>(() => Bind(Binder(() => KeyFunding(key))));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Provider, Is.EqualTo(AiProvider.OpenAi));
            Assert.That(key.CheckFailed, Is.True);
        });
    }

    [Test]
    public async Task KeyFunding_KeyRemovedSinceTheDecision_IsARejectedKey()
    {
        var key = SavedKey();
        _keys.Keys.Clear();

        await Assert.ThrowsAsync<AiKeyRejectedException>(() => Bind(Binder(() => KeyFunding(key))));
    }

    [TestCase(AiProvider.AzureOpenAi)]
    public async Task KeyFunding_ProvidersWithoutAClient_AreNotSupported(AiProvider provider)
    {
        var key = SavedKey(provider);

        await Assert.ThrowsAsync<NotSupportedException>(() => Bind(Binder(() => KeyFunding(key))));
    }

    // ------------------------------------------------------- web search (feature 40) --

    [Test]
    public async Task SearchOnAnOpenAiKey_BindsAResponsesClient_AndOnlyThatCall()
    {
        var key = SavedKey();
        var binder = Binder(() => KeyFunding(key));

        var chat = await Bind(binder);
        var search = await Bind(binder, needsWebSearch: true);
        var searchAgain = await Bind(binder, needsWebSearch: true);

#pragma warning disable OPENAI001
        Assert.Multiple(() =>
        {
            Assert.That(search.Client.GetService<OpenAI.Responses.ResponsesClient>(), Is.Not.Null, "OpenAI searches only on the Responses API");
            Assert.That(chat.Client.GetService<OpenAI.Responses.ResponsesClient>(), Is.Null, "every other call stays on Chat Completions");
            Assert.That(search.Client, Is.Not.SameAs(chat.Client));
            Assert.That(searchAgain.Client, Is.SameAs(search.Client), "the shape is part of the cache key");
            Assert.That(search.Traits, Is.SameAs(OpenAiProviderTraits.Instance));
        });
#pragma warning restore OPENAI001
    }

    [TestCase(AiProvider.Anthropic)]
    [TestCase(AiProvider.Gemini)]
    public async Task SearchOnClaudeOrGemini_KeepsTheChatClient(AiProvider provider)
    {
        var key = SavedKey(provider);
        var binder = Binder(() => KeyFunding(key, "some-model"));

        var chat = await Bind(binder);
        var search = await Bind(binder, needsWebSearch: true);

        Assert.That(search.Client, Is.SameAs(chat.Client), "their chat API takes the search tool");
    }

    [TestCase(AiFundedBy.Nornis)]
    [TestCase(AiFundedBy.Trial)]
    public async Task SearchFundedByNornisOrTheTrial_IsNeverBound(AiFundedBy fundedBy)
    {
        var binder = Binder(() => new AiFunding(AiProvider.AzureOpenAi, "nornis-ask", fundedBy));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(binder, needsWebSearch: true));
    }

    [Test]
    public async Task SearchOnAnOpenAiCompatibleKey_IsNeverBound()
    {
        var key = SavedKey(AiProvider.OpenAiCompatible, "https://models.example.com/v1/");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(Binder(() => KeyFunding(key)), needsWebSearch: true));
    }

    [Test]
    public async Task SearchOnACatalogModelThatCantSearch_IsNeverBound()
    {
        var key = SavedKey();
        var models = new AiModelsOptions
        {
            Catalog = [new AiCatalogModel { Id = "openai/gpt-small", Provider = AiProvider.OpenAi, Model = "gpt-small", Tasks = [AiTask.Prep], SupportsWebSearch = false }],
        };
        var binder = new AiCallBinder(new FixedFunding(() => KeyFunding(key) with { ModelId = "openai/gpt-small" }),
            Nornis, _userKeys, _keys, _protector, _outcomes, Microsoft.Extensions.Options.Options.Create(models), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(binder, needsWebSearch: true));
    }

    [Test]
    public void AResponsesClient_IsOnlyEverBuiltForOpenAi()
    {
        var key = SavedKey(AiProvider.Anthropic);

        Assert.Throws<NotSupportedException>(() => _userKeys.For(key, "claude-sonnet-5-5", () => "sk-ant", AiClientShape.Responses));
    }

    [Test]
    public async Task ReportRejected_MarksTheKeyThatWasUsed_NotOneSavedSince()
    {
        var key = SavedKey();
        var binder = Binder(() => KeyFunding(key));
        var binding = await Bind(binder);

        key.UpdatedAt = key.UpdatedAt.AddMinutes(1);
        await binder.ReportAsync(binding, new AiKeyRejectedException(key.Provider), CancellationToken.None);
        Assert.That(key.CheckFailed, Is.False, "the GM saved a new key while the old one was failing");

        await binder.ReportAsync(await Bind(binder), new AiKeyRejectedException(key.Provider), CancellationToken.None);
        Assert.That(key.CheckFailed, Is.True);
    }

    [Test]
    public async Task Report_CountsOutcomesPerProvider_AndNeverARefusal()
    {
        var key = SavedKey();
        var binder = Binder(() => KeyFunding(key));
        var binding = await Bind(binder);

        await binder.ReportAsync(binding, null, CancellationToken.None);
        await binder.ReportAsync(binding, new AiHttpException("boom", 500), CancellationToken.None);
        await binder.ReportAsync(binding, new AiCreditExhaustedException(key.Provider), CancellationToken.None);
        await binder.ReportAsync(binding, new AiKeyRejectedException(key.Provider), CancellationToken.None);

        var openAi = _outcomes.Snapshot(AiProvider.OpenAi, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow);
        var azure = _outcomes.Snapshot(AiProvider.AzureOpenAi, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow);
        Assert.Multiple(() =>
        {
            Assert.That((openAi.Total, openAi.Failures), Is.EqualTo((2, 1)), "the refusals say nothing about OpenAI");
            Assert.That(azure.Total, Is.Zero, "a user's key is not Nornis's resource");
        });
    }

    [Test]
    public void AiKeyProtector_RoundTrips_AndAnotherRingCantRead()
    {
        var ciphertext = _protector.Protect("sk-secret");

        Assert.Multiple(() =>
        {
            Assert.That(ciphertext, Does.Not.Contain("sk-secret"));
            Assert.That(_protector.Unprotect(ciphertext), Is.EqualTo("sk-secret"));
            Assert.That(AiKeyProtector.Create(null).Unprotect(ciphertext), Is.Null);
            Assert.That(_protector.Unprotect("not ciphertext"), Is.Null);
        });
    }

    private sealed class FixedFunding(Func<AiFunding> funding) : IAiFundingService
    {
        public Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string? nornisModel, bool needsImages, CancellationToken ct) =>
            Task.FromResult(funding());
    }
}
