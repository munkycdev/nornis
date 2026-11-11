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

    private void ChooseForAsk(string modelId) =>
        _keys.Choices.Add(new UserAiModelChoice { UserId = _creator, Task = AiTask.Ask, ModelId = modelId });

    [Test]
    public async Task AnUntestedChoice_RunsOnTheHoldersKey_PricedAsTheProvidersDearestCatalogModel()
    {
        var key = _keys.Add(_creator, AiProvider.OpenAi);
        var dearest = Model("openai/gpt-5.5", AiProvider.OpenAi);
        dearest.OutputPerMillionUsd = 30.00m;
        var images = Model("openai/gpt-image-1", AiProvider.OpenAi, true, AiTask.CoverArt);
        images.OutputPerMillionUsd = 40.00m;
        _options.Catalog.AddRange([dearest, images]);
        ChooseForAsk("untested:OpenAi/gpt-4.1-mini");

        var funding = await Resolve();

        Assert.That(funding, Is.EqualTo(new AiFunding(AiProvider.OpenAi, "gpt-4.1-mini", AiFundedBy.Key, key.Id, _creator,
            "untested:OpenAi/gpt-4.1-mini", dearest.Price)), "the image model isn't a chat model, so it sets no estimate");
    }

    [Test]
    public async Task AnUntestedChoice_IsSentImagesAsChosen_RatherThanQuietlySwitchingModels()
    {
        _keys.Add(_creator, AiProvider.OpenAi);
        ChooseForAsk("untested:OpenAi/gpt-4.1-mini");

        var funding = await Resolve(needsImages: true);

        Assert.That(funding.Model, Is.EqualTo("gpt-4.1-mini"));
    }

    [Test]
    public async Task AnUntestedChoice_TheCatalogNowLists_RunsAsTheCatalogModel()
    {
        _keys.Add(_creator, AiProvider.OpenAi);
        ChooseForAsk("untested:OpenAi/gpt-5.4-mini");

        var funding = await Resolve();

        Assert.That((funding.ModelId, funding.Price), Is.EqualTo(("openai/gpt-5.4-mini", _options.Catalog[1].Price)));
    }

    [Test]
    public async Task AnUntestedChoice_WithoutItsKey_FallsToTheTrial_OnNornissOwnModel()
    {
        ChooseForAsk("untested:Anthropic/claude-haiku-4-5");

        var funding = await Resolve();

        Assert.That((funding.FundedBy, funding.Model, funding.ModelId), Is.EqualTo((AiFundedBy.Trial, NornisModel, (string?)null)),
            "the trial never pays for a model Nornis hasn't tested");
    }

    [TestCase("untested:OpenAi/")]
    [TestCase("untested:/gpt-4.1")]
    [TestCase("untested:1/gpt-4.1")]
    [TestCase("untested:OpenAiCompatible/llama-4")]
    [TestCase("untested:AzureOpenAi/nornis-ask")]
    public async Task AMalformedUntestedChoice_IsIgnored_ForTheDefault(string modelId)
    {
        _keys.Add(_creator, AiProvider.OpenAi);
        ChooseForAsk(modelId);

        var funding = await Resolve();

        Assert.That(funding.ModelId, Is.EqualTo("openai/gpt-5.4"));
    }

    [Test]
    public void TheEstimate_IsNullForAProviderTheCatalogCantPrice() =>
        Assert.That(_options.EstimateFor(AiProvider.Anthropic), Is.Null);

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
    public async Task ARejectedKey_IsRefused_NotHandedToTheTrial()
    {
        _keys.Add(_creator, AiProvider.OpenAi, checkFailed: true);

        var ex = await Assert.ThrowsAsync<AiKeyRejectedException>(() => Resolve());

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

        var ex = await Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve());

        Assert.That(ex!.Code, Is.EqualTo("ai_key_required"));
    }

    [Test]
    public async Task TheMonthsCeiling_PausesEveryTrial_LastMonthsSpendDoesNot()
    {
        await SpendTrial(Guid.NewGuid(), 30m, at: Now.AddMonths(-1));
        await Assert.DoesNotThrowAsync(() => Resolve());

        await SpendTrial(Guid.NewGuid(), 25m);
        await Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve());
    }

    [Test]
    public async Task APublicVisitor_NeverSpendsTheTrial()
    {
        await Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve(AiCallScope.ForPublicVisitor(_worldId)));
    }

    [Test]
    public async Task APublicVisitor_SpendsTheCreatorsKey()
    {
        var key = _keys.Add(_creator, AiProvider.OpenAi);

        var funding = await Resolve(AiCallScope.ForPublicVisitor(_worldId));

        Assert.That(funding.AiKeyId, Is.EqualTo(key.Id));
    }

    [Test]
    public async Task AZeroAllowance_TurnsTheTrialOff()
    {
        _options.TrialAllowanceUsd = 0m;

        await Assert.ThrowsAsync<AiKeyRequiredException>(() => Resolve());
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

    // ------------------------------------------------------------- cover art (feature 38) --

    private Task<AiFunding> ResolveCoverArt() =>
        Service().ResolveAsync(AiCallScope.ForWorld(_worldId, Guid.NewGuid()), AiTask.CoverArt, nornisModel: null, needsImages: false,
            CancellationToken.None);

    [Test]
    public async Task CoverArt_RunsOnTheCreatorsOpenAiKey_OnTheImageModel()
    {
        _options.Catalog.Add(Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt));
        var key = _keys.Add(_creator, AiProvider.OpenAi);

        var funding = await ResolveCoverArt();

        Assert.That((funding.Model, funding.FundedBy, funding.AiKeyId), Is.EqualTo(("gpt-image-2.6-sunburst", AiFundedBy.Key, key.Id)));
    }

    [Test]
    public async Task CoverArt_TheHoldersPickOfTheSecondImageModel_WinsOverTheDefault()
    {
        _options.Catalog.Add(Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt));
        _options.Catalog.Add(Model("openai/gpt-image-2.6-flare", AiProvider.OpenAi, false, AiTask.CoverArt));
        _keys.Add(_creator, AiProvider.OpenAi);
        _keys.Choices.Add(new UserAiModelChoice { UserId = _creator, Task = AiTask.CoverArt, ModelId = "openai/gpt-image-2.6-flare" });

        var funding = await ResolveCoverArt();

        Assert.That((funding.Model, funding.ModelId), Is.EqualTo(("gpt-image-2.6-flare", "openai/gpt-image-2.6-flare")));
    }

    /// <summary>
    /// OpenAI retired gpt-image-2 for the 2.6 models, and the catalog dropped it with no data
    /// migration: a GM who had picked it is moved to the default image model, not left without one.
    /// </summary>
    [Test]
    public async Task CoverArt_APickOfARetiredImageModel_FallsBackToTheDefaultImageModel()
    {
        _options.Catalog.Add(Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt));
        _options.Catalog.Add(Model("openai/gpt-image-2.6-flare", AiProvider.OpenAi, false, AiTask.CoverArt));
        var key = _keys.Add(_creator, AiProvider.OpenAi);
        _keys.Choices.Add(new UserAiModelChoice { UserId = _creator, Task = AiTask.CoverArt, ModelId = "openai/gpt-image-2" });

        var funding = await ResolveCoverArt();

        Assert.That((funding.ModelId, funding.AiKeyId), Is.EqualTo(("openai/gpt-image-2.6-sunburst", key.Id)));
    }

    [TestCase(AiFallback.Nornis)]
    [TestCase(AiFallback.Trial)]
    public async Task CoverArt_WithNoKey_IsRefused_UnderEitherFallback(AiFallback fallback)
    {
        _options.Fallback = fallback;
        _options.Catalog.Add(Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt));

        var ex = await Assert.ThrowsAsync<AiKeyRequiredException>(ResolveCoverArt);

        Assert.That(ex!.Task, Is.EqualTo(AiTask.CoverArt));
    }

    [Test]
    public async Task CoverArt_IsNeverServedByAnOpenAiCompatibleLine()
    {
        var line = new UserAiCompatibleModel { Id = Guid.NewGuid(), Task = AiTask.CoverArt, ModelName = "m" };
        _keys.Add(_creator, AiProvider.OpenAiCompatible, false, line);

        await Assert.ThrowsAsync<AiKeyRequiredException>(ResolveCoverArt);
    }

    // ---------------------------------------------------------- refining art (feature 45) --

    [Test]
    public async Task Refining_RunsOnTheFirstImageModelThatRefines_NotThePicturesDefault()
    {
        _options.Catalog.Add(Model("openai/gpt-image-2.6-flare", AiProvider.OpenAi, false, AiTask.CoverArt));
        _options.Catalog.Add(Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt, AiTask.ArtRefinement));
        var key = _keys.Add(_creator, AiProvider.OpenAi);
        var scope = AiCallScope.ForWorld(_worldId, Guid.NewGuid());

        var pictures = await Service().ResolveAsync(scope, AiTask.CoverArt, nornisModel: null, needsImages: false, CancellationToken.None);
        var refining = await Service().ResolveAsync(scope, AiTask.ArtRefinement, nornisModel: null, needsImages: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pictures.ModelId, Is.EqualTo("openai/gpt-image-2.6-flare"));
            Assert.That((refining.ModelId, refining.FundedBy, refining.AiKeyId),
                Is.EqualTo(("openai/gpt-image-2.6-sunburst", AiFundedBy.Key, key.Id)));
        });
    }

    [TestCase(AiFallback.Nornis)]
    [TestCase(AiFallback.Trial)]
    public async Task Refining_WithNoKey_IsRefused_UnderEitherFallback(AiFallback fallback)
    {
        _options.Fallback = fallback;
        _options.Catalog.Add(Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt, AiTask.ArtRefinement));

        var ex = await Assert.ThrowsAsync<AiKeyRequiredException>(() => Service().ResolveAsync(
            AiCallScope.ForWorld(_worldId, Guid.NewGuid()), AiTask.ArtRefinement, nornisModel: null, needsImages: false, CancellationToken.None));

        Assert.That(ex!.Task, Is.EqualTo(AiTask.ArtRefinement));
    }

    [Test]
    public void CatalogValidation_KeepsImageAndChatModelsApart_ForRefiningToo()
    {
        var refinesAndChats = Model("openai/i", AiProvider.OpenAi, false, AiTask.CoverArt, AiTask.ArtRefinement, AiTask.Prep);
        var chatThatRefines = Model("openai/c", AiProvider.OpenAi, false, AiTask.Prep, AiTask.ArtRefinement);
        var imageThatRefines = Model("openai/ok", AiProvider.OpenAi, false, AiTask.CoverArt, AiTask.ArtRefinement);

        Assert.Multiple(() =>
        {
            Assert.That(new AiModelsOptions { Catalog = [refinesAndChats] }.StartupProblem(), Does.Contain("only CoverArt and ArtRefinement"));
            Assert.That(new AiModelsOptions { Catalog = [chatThatRefines] }.StartupProblem(), Does.Contain("only an image model can"));
            Assert.That(new AiModelsOptions { Catalog = [imageThatRefines] }.StartupProblem(), Is.Null);
        });
    }

    [Test]
    public void CatalogValidation_RefusesAnImageModelThatAlsoServesChat()
    {
        var mixed = Model("openai/gpt-image-2.6-sunburst", AiProvider.OpenAi, false, AiTask.CoverArt, AiTask.Prep);

        Assert.That(new AiModelsOptions { Catalog = [mixed] }.StartupProblem(), Does.Contain("only CoverArt"));
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
            Assert.That(new AiModelsOptions { Catalog = [Model("azure/c", AiProvider.AzureOpenAi)] }.StartupProblem(),
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

    // ------------------------------------------------------- web search (feature 40) --

    [Test]
    public void Price_AddsSearchesAtTheirOwnRate_AndAnUnreportedCountAsNone()
    {
        var price = new AiModelPrice(2.50m, 0.25m, 15.00m, WebSearchPerThousandUsd: 10.00m);

        Assert.Multiple(() =>
        {
            // 100k input, 10k output: 0.25 + 0.15 = 0.40, plus 3 searches at $10 per 1,000 = 0.03.
            Assert.That(price.Cost(100_000, null, 10_000, webSearches: 3), Is.EqualTo(0.43m));
            Assert.That(price.Cost(100_000, null, 10_000, webSearches: null), Is.EqualTo(0.40m));
            Assert.That(new AiModelPrice(2.50m, 0.25m, 15.00m).Cost(100_000, null, 10_000, webSearches: 3), Is.EqualTo(0.40m),
                "a model with no search price can't be charged for searches it can't run");
        });
    }

    [Test]
    public void CatalogValidation_RefusesASearchModelWithoutASearchPrice_AndAPriceWithoutSearch()
    {
        var unpriced = Model("openai/s", AiProvider.OpenAi);
        unpriced.SupportsWebSearch = true;
        var stray = Model("openai/t", AiProvider.OpenAi);
        stray.WebSearchPerThousandUsd = 10m;
        var notPrep = Model("openai/u", AiProvider.OpenAi, true, AiTask.Ask);
        notPrep.SupportsWebSearch = true;
        notPrep.WebSearchPerThousandUsd = 10m;
        var good = Model("openai/v", AiProvider.OpenAi);
        good.SupportsWebSearch = true;
        good.WebSearchPerThousandUsd = 10m;

        Assert.Multiple(() =>
        {
            Assert.That(new AiModelsOptions { Catalog = [unpriced] }.StartupProblem(), Does.Contain("no search price"));
            Assert.That(new AiModelsOptions { Catalog = [stray] }.StartupProblem(), Does.Contain("doesn't search"));
            Assert.That(new AiModelsOptions { Catalog = [notPrep] }.StartupProblem(), Does.Contain("doesn't serve Prep"));
            Assert.That(new AiModelsOptions { Catalog = [good] }.StartupProblem(), Is.Null);
        });
    }

    [Test]
    public void TheEstimate_SearchesAtTheProvidersDearestSearchPrice()
    {
        var dearTokens = Model("openai/big", AiProvider.OpenAi);
        dearTokens.OutputPerMillionUsd = 40m;
        var cheapSearcher = Model("openai/small", AiProvider.OpenAi);
        cheapSearcher.SupportsWebSearch = true;
        cheapSearcher.WebSearchPerThousandUsd = 10m;
        var dearSearcher = Model("openai/mid", AiProvider.OpenAi);
        dearSearcher.SupportsWebSearch = true;
        dearSearcher.WebSearchPerThousandUsd = 25m;

        var estimate = new AiModelsOptions { Catalog = [cheapSearcher, dearTokens, dearSearcher] }.EstimateFor(AiProvider.OpenAi);

        Assert.Multiple(() =>
        {
            Assert.That(estimate?.OutputPerMillionUsd, Is.EqualTo(40m), "tokens still from the dearest model");
            Assert.That(estimate?.WebSearchPerThousandUsd, Is.EqualTo(25m), "searches from the dearest searcher, though another model is dearer per token");
        });
    }

    [Test]
    public async Task TheRecorder_PricesAKeyFundedCallsSearches()
    {
        var repository = new InMemoryAiUsageRecordRepository();
        var recorder = TestUsageRecorder.Wrap(repository);
        var price = new AiModelPrice(2.50m, null, 15.00m, WebSearchPerThousandUsd: 10.00m);

        await recorder.RecordAsync(Guid.NewGuid(), Guid.NewGuid(), Domain.Enums.AiOperationType.AdventureConversation,
            new AiUsage
            {
                Model = "gpt-5.4",
                InputTokens = 0,
                OutputTokens = 0,
                TotalTokens = 0,
                DurationMs = 1,
                WebSearches = 5,
                Funding = new AiFunding(AiProvider.OpenAi, "gpt-5.4", AiFundedBy.Key, Price: price),
            },
            succeeded: true, errorCode: null);

        Assert.That(repository.Records.Single().EstimatedCostUsd, Is.EqualTo(0.05m));
    }

    [Test]
    public void TheShippedCatalog_MarksOnlyPrepModelsAsSearching_AndPricesThem()
    {
        var options = ShippedCatalog();
        string[] searching =
        [
            "openai/gpt-5.4", "anthropic/claude-sonnet-5-5", "anthropic/claude-opus-5-5",
            "gemini/gemini-3.8-flash", "gemini/gemini-4-argon",
        ];

        Assert.Multiple(() =>
        {
            Assert.That(options.StartupProblem(), Is.Null);
            Assert.That(options.Catalog.Where(m => m.SupportsWebSearch).Select(m => m.Id), Is.EquivalentTo(searching));
        });
    }

    private static AiModelsOptions ShippedCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root");
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Nornis.Api", "appsettings.json"));
        var section = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("AiModels").GetRawText();
        return System.Text.Json.JsonSerializer.Deserialize<AiModelsOptions>(section, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        })!;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
