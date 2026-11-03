using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Accounts;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Application.Tests.Accounts;

/// <summary>
/// Profile's AI keys (feature 39): what a save accepts, that a key is checked before it is
/// stored, that re-saving keeps what the user didn't change, and when a save queues the sources
/// that failed for want of a key. The API tests cover the same rules end to end.
/// </summary>
[TestFixture]
public class AiKeyServiceTests
{
    private const string Secret = "sk-test-secret-WXYZ";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-20T12:00:00Z");

    private InMemoryAiKeyRepository _keys = null!;
    private InMemoryAiUsageRecordRepository _usage = null!;
    private InMemoryWorldRepository _worlds = null!;
    private InMemorySourceRepository _sources = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private ISourceService _sourceService = null!;
    private RecordingChecker _checker = null!;
    private AiModelsOptions _options = null!;
    private IAiKeyProtector _protector = null!;
    private Guid _user;

    [SetUp]
    public void SetUp()
    {
        _keys = new InMemoryAiKeyRepository();
        _usage = new InMemoryAiUsageRecordRepository();
        _worlds = new InMemoryWorldRepository();
        _sources = new InMemorySourceRepository();
        _members = new InMemoryWorldMemberRepository();
        _sourceService = Substitute.For<ISourceService>();
        _sourceService.MarkReadyAsync(Arg.Any<MarkSourceReadyCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => AppResult<Source>.Success(new Source { Id = call.Arg<MarkSourceReadyCommand>().SourceId }));
        _checker = new RecordingChecker();
        _protector = new PlainProtector();
        _options = new AiModelsOptions
        {
            Catalog =
            [
                new AiCatalogModel
                {
                    Id = "openai/gpt-5.4", Provider = AiProvider.OpenAi, Model = "gpt-5.4",
                    Tasks = [AiTask.Ask, AiTask.Extraction, AiTask.Prep, AiTask.Chores], SupportsImages = true,
                    InputPerMillionUsd = 2.50m, OutputPerMillionUsd = 15.00m,
                },
            ],
        };
        _user = Guid.NewGuid();
    }

    private AiKeyService Service() => new(
        _keys, _protector, _checker, _usage, _worlds, _sources, _members, _sourceService,
        Options.Create(_options), new FixedTime(Now), NullLogger<AiKeyService>.Instance);

    private Task<AppResult<SavedAiKey>> Save(
        AiProvider provider = AiProvider.OpenAi, string? key = Secret, string? baseUrl = null,
        params AiCompatibleModelLine[] models) =>
        Service().SaveAsync(new SaveAiKeyCommand(_user, provider, key, null, baseUrl,
            provider == AiProvider.OpenAiCompatible || models.Length > 0 ? models : null), CancellationToken.None);

    private static AiCompatibleModelLine Line(AiTask task, string name = "llama-4", bool images = false) =>
        new(task, name, images, 0.50m, 1.50m);

    [Test]
    public async Task A_key_is_stored_protected_with_its_last_four_after_the_provider_accepts_it()
    {
        var result = await Save();

        var stored = _keys.Keys.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_checker.Checks.Single(), Is.EqualTo((AiProvider.OpenAi, Secret, (Uri?)null)));
            Assert.That(stored.ProtectedKey, Is.EqualTo("protected:" + Secret));
            Assert.That(stored.LastFour, Is.EqualTo("WXYZ"));
            Assert.That(stored.Label, Is.EqualTo("OpenAI"), "a provider's name when the user gives none");
            Assert.That(stored.CheckFailed, Is.False);
        });
    }

    [TestCase(AiKeyCheckOutcome.Rejected, "ai_key_check_failed")]
    [TestCase(AiKeyCheckOutcome.NotFound, "ai_endpoint_not_found")]
    [TestCase(AiKeyCheckOutcome.Unreachable, "ai_key_check_unreachable")]
    public async Task A_key_the_check_refuses_is_not_stored(AiKeyCheckOutcome outcome, string code)
    {
        _checker.Outcome = outcome;

        var result = await Save();

        Assert.That(result.Error?.Code, Is.EqualTo(code));
        Assert.That(_keys.Keys, Is.Empty);
    }

    /// <summary>Seen on prod the first time: the ring's blob refused its first write because the
    /// storage role hadn't taken effect, and the save answered a bare 500.</summary>
    [Test]
    public async Task A_key_ring_that_cant_be_written_saves_nothing_and_says_to_try_again()
    {
        _protector = new UnwritableProtector();

        var result = await Save();

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo("ai_key_storage_unavailable"));
            Assert.That(result.Error?.StatusCode, Is.EqualTo(503));
            Assert.That(_keys.Keys, Is.Empty);
        });
    }

    [TestCase("sk short")]
    [TestCase("short")]
    public async Task A_key_that_cant_be_one_is_refused_before_any_check(string key)
    {
        var result = await Save(key: key);

        Assert.That(result.Error?.Code, Is.EqualTo("invalid_key"));
        Assert.That(_checker.Checks, Is.Empty);
    }

    [Test]
    public async Task Saving_without_a_key_checks_the_stored_one_again_and_clears_a_failed_check()
    {
        await Save();
        _keys.Keys.Single().CheckFailed = true;

        var result = await Save(key: null);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_checker.Checks.Select(c => c.Key), Is.EqualTo([Secret, Secret]));
        Assert.That(_keys.Keys.Single().CheckFailed, Is.False);
    }

    [Test]
    public async Task Saving_without_a_key_when_there_is_none_asks_for_one()
    {
        var result = await Save(key: "");

        Assert.That(result.Error?.Code, Is.EqualTo("key_required"));
    }

    [TestCase("http://llm.example.com/v1/")]
    [TestCase("https://user:pass@llm.example.com/v1/")]
    [TestCase("https://llm.example.com/v1/?api-version=1")]
    [TestCase("llm.example.com/v1")]
    [TestCase(null)]
    public async Task A_compatible_key_needs_a_plain_https_base_url(string? baseUrl)
    {
        var result = await Save(AiProvider.OpenAiCompatible, baseUrl: baseUrl, models: Line(AiTask.Ask));

        Assert.That(result.Error?.Code, Is.EqualTo("invalid_base_url"));
        Assert.That(_checker.Checks, Is.Empty);
    }

    [Test]
    public async Task A_compatible_base_url_gets_its_trailing_slash_so_the_sdk_keeps_its_last_segment()
    {
        await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/openai/v1", models: Line(AiTask.Ask));

        Assert.That(_checker.Checks.Single().BaseUrl, Is.EqualTo(new Uri("https://llm.example.com/openai/v1/")));
        Assert.That(_keys.Keys.Single().BaseUrl, Is.EqualTo("https://llm.example.com/openai/v1/"));
    }

    [Test]
    public async Task Only_a_compatible_key_takes_a_base_url_or_models()
    {
        var withUrl = await Save(baseUrl: "https://api.openai.com/v1/");
        var withModels = await Save(models: Line(AiTask.Ask));

        Assert.That(withUrl.Error?.Code, Is.EqualTo("invalid_base_url"));
        Assert.That(withModels.Error?.Code, Is.EqualTo("invalid_models"));
    }

    [Test]
    public async Task A_compatible_key_names_one_model_per_task_and_at_least_one()
    {
        var none = await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/v1/");
        var twice = await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/v1/", models: [Line(AiTask.Ask), Line(AiTask.Ask, "other")]);
        var negative = await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/v1/",
            models: new AiCompatibleModelLine(AiTask.Ask, "llama-4", false, -1m, 1m));

        Assert.That(new[] { none, twice, negative }.Select(r => r.Error?.Code), Is.All.EqualTo("invalid_models"));
    }

    [Test]
    public async Task Resaving_a_compatible_key_keeps_the_line_a_choice_names()
    {
        await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/v1/", models: [Line(AiTask.Ask), Line(AiTask.Prep)]);
        var askLine = _keys.Keys.Single().CompatibleModels.Single(l => l.Task == AiTask.Ask).Id;
        await Service().ChooseModelAsync(_user, AiTask.Ask, "compatible:" + askLine, CancellationToken.None);

        await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/v1/", models: Line(AiTask.Ask, "llama-5"));

        var models = await Service().ListModelsAsync(_user, CancellationToken.None);
        var ask = models.Single(t => t.Task == AiTask.Ask);
        Assert.Multiple(() =>
        {
            Assert.That(ask.ChosenId, Is.EqualTo("compatible:" + askLine));
            Assert.That(ask.Options.Single(o => o.Id == ask.ChosenId).Name, Is.EqualTo("llama-5"));
            Assert.That(ask.Options.Single(o => o.Id == ask.ChosenId).Tested, Is.False, "Nornis has never run it");
            Assert.That(models.Single(t => t.Task == AiTask.Prep).Options.Where(o => !o.Tested), Is.Empty, "dropped line");
        });
    }

    [Test]
    public async Task Another_users_line_cant_be_chosen()
    {
        var mire = Guid.NewGuid();
        var line = new UserAiCompatibleModel { Id = Guid.NewGuid(), Task = AiTask.Ask, ModelName = "theirs" };
        _keys.Add(mire, AiProvider.OpenAiCompatible, lines: line);

        var result = await Service().ChooseModelAsync(_user, AiTask.Ask, "compatible:" + line.Id, CancellationToken.None);

        Assert.That(result.Error?.Code, Is.EqualTo("invalid_model"));
        Assert.That(_keys.Choices, Is.Empty);
    }

    [Test]
    public async Task Saving_a_key_queues_key_failed_sources_in_the_users_worlds_through_mark_ready()
    {
        var world = Guid.NewGuid();
        _sources.WorldCreators[world] = _user;
        await _members.CreateAsync(new WorldMember { Id = Guid.NewGuid(), WorldId = world, UserId = _user, Role = WorldRole.GM });
        var needsKey = Failed(world, ErrorCategories.AiKeyRequired);
        var outOfCredit = Failed(world, ErrorCategories.AiCreditExhausted);
        Failed(world, ErrorCategories.ParseFailure);

        var result = await Save();

        Assert.That(result.Value!.SourcesQueued, Is.EqualTo(2));
        await _sourceService.Received(1).MarkReadyAsync(
            new MarkSourceReadyCommand(needsKey, world, _user, WorldRole.GM), Arg.Any<CancellationToken>());
        await _sourceService.Received(1).MarkReadyAsync(
            new MarkSourceReadyCommand(outOfCredit, world, _user, WorldRole.GM), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Saving_a_key_that_cant_run_extraction_queues_nothing()
    {
        var world = Guid.NewGuid();
        _sources.WorldCreators[world] = _user;
        await _members.CreateAsync(new WorldMember { Id = Guid.NewGuid(), WorldId = world, UserId = _user, Role = WorldRole.GM });
        Failed(world, ErrorCategories.AiKeyRequired);

        var result = await Save(AiProvider.OpenAiCompatible, baseUrl: "https://llm.example.com/v1/", models: Line(AiTask.Ask));

        Assert.That(result.Value!.SourcesQueued, Is.Zero, "they would only fail again");
        await _sourceService.DidNotReceiveWithAnyArgs().MarkReadyAsync(default!, default);
    }

    [Test]
    public async Task The_trial_view_counts_what_the_user_spent_and_the_months_ceiling()
    {
        _options.Fallback = AiFallback.Trial;
        await SpendTrial(_user, 0.60m);

        var trial = await Service().GetTrialAsync(_user, CancellationToken.None);
        await SpendTrial(Guid.NewGuid(), _options.TrialMonthlyCeilingUsd);
        var paused = await Service().GetTrialAsync(_user, CancellationToken.None);

        Assert.That(trial, Is.EqualTo(new AiTrialView(true, 2.00m, 0.60m, 1.40m, Available: true)));
        Assert.That(paused.Available, Is.False, "every trial is paused once the month's ceiling is spent");
    }

    [Test]
    public async Task A_world_is_on_the_trial_until_its_creator_has_a_key_for_every_task()
    {
        _options.Fallback = AiFallback.Trial;
        var world = new World { Id = Guid.NewGuid(), Name = "Gribbits", CreatedByUserId = _user };
        await _worlds.CreateAsync(world);
        await SpendTrial(_user, 0.25m);

        var before = await Service().GetWorldFundingAsync(world.Id, _user, WorldRole.GM, CancellationToken.None);
        await Save();
        var after = await Service().GetWorldFundingAsync(world.Id, Guid.NewGuid(), WorldRole.GM, CancellationToken.None);
        var player = await Service().GetWorldFundingAsync(world.Id, Guid.NewGuid(), WorldRole.Player, CancellationToken.None);

        Assert.That(before.Value, Is.EqualTo(new WorldAiFundingView(true, 1.75m, CreatorIsYou: true)));
        Assert.That(after.Value, Is.EqualTo(new WorldAiFundingView(false, null, CreatorIsYou: false)));
        Assert.That(player.Error?.StatusCode, Is.EqualTo(403), "players are told nothing about money");
    }

    private Guid Failed(Guid world, string category)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world,
            Title = category,
            ProcessingStatus = SourceProcessingStatus.Failed,
            FailureCategory = category,
        };
        _sources.Seed(source);
        return source.Id;
    }

    private Task SpendTrial(Guid holder, decimal usd) =>
        _usage.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            KeyHolderUserId = holder,
            FundedBy = AiFundedBy.Trial,
            EstimatedCostUsd = usd,
            CreatedAt = Now,
        });

    private sealed class RecordingChecker : IAiKeyChecker
    {
        public AiKeyCheckOutcome Outcome { get; set; } = AiKeyCheckOutcome.Accepted;

        public List<(AiProvider Provider, string Key, Uri? BaseUrl)> Checks { get; } = [];

        public Task<AiKeyCheckOutcome> CheckAsync(AiProvider provider, string key, Uri? baseUrl, CancellationToken ct)
        {
            Checks.Add((provider, key, baseUrl));
            return Task.FromResult(Outcome);
        }
    }

    private sealed class PlainProtector : IAiKeyProtector
    {
        public string Protect(string key) => "protected:" + key;

        public string? Unprotect(string protectedKey) =>
            protectedKey.StartsWith("protected:", StringComparison.Ordinal) ? protectedKey["protected:".Length..] : null;
    }

    private sealed class UnwritableProtector : IAiKeyProtector
    {
        public string Protect(string key) => throw new AiKeyStorageException(new InvalidOperationException("403 from the blob"));

        public string? Unprotect(string protectedKey) => null;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
