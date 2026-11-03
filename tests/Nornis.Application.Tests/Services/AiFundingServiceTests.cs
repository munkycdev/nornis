using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>Each branch of the funding rule (feature 39, design "Funding rule").</summary>
[TestFixture]
public class AiFundingServiceTests
{
    private const string NornisModel = "nornis-ask";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-20T12:00:00Z");

    private InMemoryAiKeyRepository _keys = null!;
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryAiUsageRecordRepository _usage = null!;
    private AiModelsOptions _options = null!;
    private Guid _creator;
    private Guid _worldId;

    private static AiCatalogModel Model(string id, AiProvider provider, bool images = true, params AiTask[] tasks) => new()
    {
        Id = id,
        Provider = provider,
        Model = id.Split('/')[1],
        Tasks = tasks.Length == 0 ? [AiTask.Ask, AiTask.Extraction, AiTask.Prep, AiTask.Chores] : [.. tasks],
        SupportsImages = images,
        InputPerMillionUsd = 2.50m,
        CachedInputPerMillionUsd = 0.25m,
        OutputPerMillionUsd = 15.00m,
    };

    [SetUp]
    public async Task SetUp()
    {
        _keys = new InMemoryAiKeyRepository();
        _worlds = new InMemoryWorldRepository();
        _usage = new InMemoryAiUsageRecordRepository();
        _options = new AiModelsOptions
        {
            Fallback = AiFallback.Trial,
            Catalog =
            [
                Model("openai/gpt-5.4", AiProvider.OpenAi),
                Model("openai/gpt-5.4-mini", AiProvider.OpenAi, images: false),
            ],
        };
        _creator = Guid.NewGuid();
        _worldId = Guid.NewGuid();
        await _worlds.CreateAsync(new World { Id = _worldId, Name = "Gribbits", CreatedByUserId = _creator });
    }

    private AiFundingService Service() =>
        new(_keys, _worlds, _usage, Options.Create(_options), new FixedTime(Now));

    private Task<AiFunding> Resolve(AiCallScope? scope = null, AiTask task = AiTask.Ask, bool needsImages = false) =>
        Service().ResolveAsync(scope ?? AiCallScope.ForWorld(_worldId, Guid.NewGuid()), task, NornisModel, needsImages, CancellationToken.None);

    private Task SpendTrial(Guid holder, decimal usd, DateTimeOffset? at = null) =>
        _usage.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            KeyHolderUserId = holder,
            FundedBy = AiFundedBy.Trial,
            EstimatedCostUsd = usd,
            CreatedAt = at ?? Now,
        });

    [Test]
    public async Task AWorldsCall_SpendsItsCreatorsKey_NotTheAskers()
    {
        var key = _keys.Add(_creator, AiProvider.OpenAi);
        var asker = Guid.NewGuid();
        _keys.Add(asker, AiProvider.OpenAi);

        var funding = await Resolve(AiCallScope.ForWorld(_worldId, asker));

        Assert.That(funding, Is.EqualTo(new AiFunding(AiProvider.OpenAi, "gpt-5.4", AiFundedBy.Key, key.Id, _creator,
            "openai/gpt-5.4", _options.Catalog[0].Price)));
    }

    [Test]
    public async Task ACallWithNoWorld_SpendsTheCallersKey()
    {
        var user = Guid.NewGuid();
        var key = _keys.Add(user, AiProvider.OpenAi);

        var funding = await Resolve(AiCallScope.ForUser(user), AiTask.Chores);

        Assert.That((funding.AiKeyId, funding.KeyHolderUserId), Is.EqualTo((key.Id, user)));
    }

    [Test]
    public async Task TheHoldersChoice_WinsOverTheDefault()
    {
        _keys.Add(_creator, AiProvider.OpenAi);
        _keys.Choices.Add(new UserAiModelChoice { UserId = _creator, Task = AiTask.Ask, ModelId = "openai/gpt-5.4-mini" });

        var funding = await Resolve();

        Assert.That((funding.Model, funding.ModelId), Is.EqualTo(("gpt-5.4-mini", "openai/gpt-5.4-mini")));
    }

    [Test]
    public async Task AChoiceThatCantReadImages_FallsBackToTheDefault_ForACallWithImages()
    {
        _keys.Add(_creator, AiProvider.OpenAi);
        _keys.Choices.Add(new UserAiModelChoice { UserId = _creator, Task = AiTask.Extraction, ModelId = "openai/gpt-5.4-mini" });

        var funding = await Resolve(task: AiTask.Extraction, needsImages: true);

        Assert.That(funding.ModelId, Is.EqualTo("openai/gpt-5.4"));
    }

    [Test]
    public async Task AChoiceThatLeftTheCatalog_FallsBackToTheDefault_NeverOff()
    {
        _keys.Add(_creator, AiProvider.OpenAi);
        _keys.Choices.Add(new UserAiModelChoice { UserId = _creator, Task = AiTask.Ask, ModelId = "openai/gone" });

        var funding = await Resolve();

        Assert.That(funding.ModelId, Is.EqualTo("openai/gpt-5.4"));
    }

    [Test]
    public async Task ACompatibleKey_RunsItsLineForTheTask_AtTheUsersOwnPrice()
    {
        var line = new UserAiCompatibleModel
        {
            Id = Guid.NewGuid(),
            Task = AiTask.Ask,
            ModelName = "my-deployment",
            InputPerMillionUsd = 1m,
            OutputPerMillionUsd = 4m,
        };
        var key = _keys.Add(_creator, AiProvider.OpenAiCompatible, false, line);

        var funding = await Resolve();

        Assert.That(funding, Is.EqualTo(new AiFunding(AiProvider.OpenAiCompatible, "my-deployment", AiFundedBy.Key, key.Id,
            _creator, null, new AiModelPrice(1m, null, 4m))));
    }

    [Test]
    public void ARejectedKey_IsRefused_NotHandedToTheTrial()
    {
        _keys.Add(_creator, AiProvider.OpenAi, checkFailed: true);

        var ex = Assert.ThrowsAsync<AiKeyRejectedException>(() => Resolve());

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Provider, Is.EqualTo(AiProvider.OpenAi));
            Assert.That(ex.Code, Is.EqualTo("ai_key_rejected"));
            Assert.That(ex.Category, Is.EqualTo(ErrorCategories.AiKeyRejected));
        });
    }

    [Test]
    public async Task NoKey_FallbackNornis_NornisPaysOnItsOwnModel()
    {
        _options.Fallback = AiFallback.Nornis;

        var funding = await Resolve();

        Assert.That(funding, Is.EqualTo(new AiFunding(AiProvider.AzureOpenAi, NornisModel, AiFundedBy.Nornis, KeyHolderUserId: _creator)));
    }

    [Test]
    public async Task NoKey_TheTrialPays_OnNornisModel()
    {
        await SpendTrial(_creator, 1.99m);

        var funding = await Resolve();

        Assert.That(funding, Is.EqualTo(new AiFunding(AiProvider.AzureOpenAi, NornisModel, AiFundedBy.Trial, KeyHolderUserId: _creator)));
    }

    [Test]
    public async Task TheTrialSpent_IsRefused_EvenIfTheLastCallWentBelowZero()
    {
        await SpendTrial(_creator, 2.07m);

        var ex = Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve());

        Assert.That(ex!.Code, Is.EqualTo("ai_key_required"));
    }

    [Test]
    public async Task TheMonthsCeiling_PausesEveryTrial_LastMonthsSpendDoesNot()
    {
        await SpendTrial(Guid.NewGuid(), 30m, at: Now.AddMonths(-1));
        Assert.DoesNotThrowAsync(() => Resolve());

        await SpendTrial(Guid.NewGuid(), 25m);
        Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve());
    }

    [Test]
    public void APublicVisitor_NeverSpendsTheTrial()
    {
        Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve(AiCallScope.ForPublicVisitor(_worldId)));
    }

    [Test]
    public async Task APublicVisitor_SpendsTheCreatorsKey()
    {
        var key = _keys.Add(_creator, AiProvider.OpenAi);

        var funding = await Resolve(AiCallScope.ForPublicVisitor(_worldId));

        Assert.That(funding.AiKeyId, Is.EqualTo(key.Id));
    }

    [Test]
    public void AZeroAllowance_TurnsTheTrialOff()
    {
        _options.TrialAllowanceUsd = 0m;

        Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve());
    }

    [Test]
    public async Task AKeyForAnotherTask_DoesNotCoverThisOne()
    {
        // Only Ask is served by the compatible line; Extraction falls to the trial.
        var line = new UserAiCompatibleModel { Id = Guid.NewGuid(), Task = AiTask.Ask, ModelName = "m" };
        _keys.Add(_creator, AiProvider.OpenAiCompatible, false, line);

        var funding = await Resolve(task: AiTask.Extraction);

        Assert.That(funding.FundedBy, Is.EqualTo(AiFundedBy.Trial));
    }

    [Test]
    public void CatalogValidation_RefusesAnUnpricedOrUnservedOrDuplicateEntry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_options.StartupProblem(), Is.Null);
            Assert.That(new AiModelsOptions { Catalog = [Model("openai/a", AiProvider.OpenAi), Model("openai/a", AiProvider.OpenAi)] }
                .StartupProblem(), Does.Contain("twice"));
            var unpriced = Model("openai/b", AiProvider.OpenAi);
            unpriced.OutputPerMillionUsd = 0m;
            Assert.That(new AiModelsOptions { Catalog = [unpriced] }.StartupProblem(), Does.Contain("no price"));
            Assert.That(new AiModelsOptions { Catalog = [Model("anthropic/c", AiProvider.Anthropic)] }.StartupProblem(),
                Does.Contain("no client"));
        });
    }

    [Test]
    public void Price_ChargesCachedInputAtItsOwnRate()
    {
        var price = new AiModelPrice(2.50m, 0.25m, 15.00m);

        // 1M input of which 400k cached, 100k output: 0.6*2.50 + 0.4*0.25 + 0.1*15 = 3.10
        Assert.That(price.Cost(1_000_000, 400_000, 100_000), Is.EqualTo(3.10m));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
