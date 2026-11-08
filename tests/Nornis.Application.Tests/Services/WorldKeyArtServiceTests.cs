using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Art;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.KeyArt;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// A world's key art (feature 42): uploads are checked from their bytes, generation is landscape,
/// capped per month and spent from Prep, and a world never holds more than the chosen art plus one
/// generation's options (correctness property 3). Every write is the GM's.
/// </summary>
[TestFixture]
public class WorldKeyArtServiceTests
{
    private static readonly Guid GmUserId = Guid.NewGuid();
    private const string Brief = "A storm-dark coast at dusk, a lighthouse on the headland, the sea glittering under a broken sky.";

    private InMemoryWorldRepository _worlds = null!;
    private FakeImageArtGenerator _generator = null!;
    private FakeBlobStorageService _blobs = null!;
    private FakeAiBudgetGuard _budget = null!;
    private InMemoryAiUsageRecordRepository _ledger = null!;
    private KeyArtOptions _options = null!;
    private MutableTime _time = null!;
    private WorldKeyArtService _sut = null!;
    private Guid _worldId;

    [SetUp]
    public async Task SetUp()
    {
        _worlds = new InMemoryWorldRepository();
        _generator = new FakeImageArtGenerator();
        _blobs = new FakeBlobStorageService();
        _budget = new FakeAiBudgetGuard();
        _ledger = new InMemoryAiUsageRecordRepository();
        _options = new KeyArtOptions();
        _time = new MutableTime(DateTimeOffset.Parse("2026-11-07T12:00:00Z"));
        _sut = new WorldKeyArtService(_worlds, new InMemoryWorldKeyArtRepository(_worlds), _generator, _blobs, _budget,
            TestUsageRecorder.Wrap(_ledger), Options.Create(_options), _time, NullLogger<WorldKeyArtService>.Instance);
        _worldId = Guid.NewGuid();
        await _worlds.CreateAsync(new World { Id = _worldId, Name = "Black Harbor", CreatedByUserId = GmUserId });
    }

    private World Stored => _worlds.Worlds.Single();

    private Task<AppResult<KeyArtState>> GenerateAsync(string brief = Brief, WorldRole role = WorldRole.GM) =>
        _sut.GenerateAsync(new GenerateKeyArtCommand(_worldId, GmUserId, role, brief), default);

    private Task<AppResult<KeyArtState>> UploadAsync(byte[] bytes, WorldRole role = WorldRole.GM) =>
        _sut.UploadAsync(new UploadKeyArtCommand(_worldId, GmUserId, role, bytes), default);

    private Task<AppResult<KeyArtState>> ChooseAsync(Guid option) =>
        _sut.ChooseAsync(new ChooseKeyArtCommand(_worldId, GmUserId, WorldRole.GM, option), default);

    [Test]
    public async Task Generate_MakesTwoLandscapeOptions_WithTheRulesAfterTheBrief_AndCountsThem()
    {
        var state = (await GenerateAsync()).Value!;

        var call = _generator.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Count, Is.EqualTo(2));
            Assert.That(_generator.Shapes.Single(), Is.EqualTo((ImageShape.Landscape, nameof(AiOperationType.KeyArt))));
            Assert.That(call.Prompt, Does.StartWith(Brief));
            Assert.That(call.Prompt, Does.EndWith(KeyArtPrompt.ImageRules));
            Assert.That(call.Scope.WorldId, Is.EqualTo(_worldId));
            Assert.That(state.Options, Has.Count.EqualTo(2));
            Assert.That(state.ImagesThisMonth, Is.EqualTo(2));
            Assert.That(state.Brief, Is.EqualTo(Brief));
            Assert.That(state.KeyArt, Is.Null, "an option is not the art until the GM picks it");
            Assert.That(state.Options.Select(id => KeyArtBlobs.OptionPath(_worldId, id)), Is.SubsetOf(_blobs.Blobs.Keys));
            Assert.That(_ledger.Records.Single().OperationType, Is.EqualTo(AiOperationType.KeyArt));
            Assert.That(_budget.PrepChecks, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Generate_WithNoImageKey_SaysSoOnTheCard()
    {
        _generator.HasKey = false;

        var state = (await _sut.GetAsync(_worldId, GmUserId, WorldRole.GM, default)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.GenerateBlocker, Is.EqualTo(WorldKeyArtService.NoKeyHint));
        });
    }

    [Test]
    public async Task TheCap_OnlyMakesWhatIsLeft_ThenRefuses_AndANewMonthStartsAgain()
    {
        _options.MaxImagesPerMonth = 5;
        await GenerateAsync();
        await GenerateAsync();

        var third = (await GenerateAsync()).Value!;
        var fourth = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_generator.Calls.Select(c => c.Count), Is.EqualTo([2, 2, 1]));
            Assert.That(third.ImagesThisMonth, Is.EqualTo(5));
            Assert.That(third.CanGenerate, Is.False);
            Assert.That(third.GenerateBlocker, Does.Contain("upload your own art"));
            Assert.That(fourth.Error!.Code, Is.EqualTo("key_art_cap"));
            Assert.That(_generator.Calls, Has.Count.EqualTo(3), "a refused generation never reaches the model");
        });

        _time.Now = DateTimeOffset.Parse("2026-12-01T00:00:01Z");
        var december = (await GenerateAsync()).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(december.ImagesThisMonth, Is.EqualTo(2), "last month's images are spent, not owed");
            Assert.That(Stored.KeyArtImagesMonth, Is.EqualTo("2026-12"));
        });
    }

    [Test]
    public async Task AFailedCall_GivesTheImagesBack_AndNamesTheProblem()
    {
        _generator.Throw = new ImageArtRejectedException(moderation: true, "safety system");

        var result = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("key_art_refused"));
            Assert.That(result.Error.Message, Does.Contain("Try rewording"));
            Assert.That(Stored.KeyArtImagesGenerated, Is.EqualTo(0));
            Assert.That(_ledger.Records.Single().Succeeded, Is.False);
            Assert.That(_blobs.Blobs, Is.Empty);
        });
    }

    [Test]
    public async Task AShortDelivery_GivesBackWhatDidNotCome()
    {
        _generator.Deliver = 1;

        var state = (await GenerateAsync()).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(state.Options, Has.Count.EqualTo(1));
            Assert.That(state.ImagesThisMonth, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Generate_RefusesABlankBrief_AndWaitsOnThePrepBudget()
    {
        var blank = await GenerateAsync("   ");
        _budget.PrepExceeded = true;
        var broke = await GenerateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(blank.Error!.Code, Is.EqualTo("invalid_brief"));
            Assert.That(broke.Error!.Code, Is.EqualTo("prep_budget_exceeded"));
            Assert.That(_generator.Calls, Is.Empty);
            Assert.That(Stored.KeyArtImagesGenerated, Is.EqualTo(0), "nothing is counted for a call that never ran");
        });
    }

    [Test]
    public async Task Choose_PointsTheArtAtTheOption_AndGeneratingAgainKeepsOnlyTheChosenOne()
    {
        var first = (await GenerateAsync()).Value!;
        var chosen = first.Options[0];
        var unchosen = first.Options[1];
        var afterChoose = (await ChooseAsync(chosen)).Value!;

        var second = (await GenerateAsync()).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(afterChoose.KeyArt!.Source, Is.EqualTo(CoverSource.Generated));
            Assert.That(afterChoose.KeyArt.BlobPath, Is.EqualTo(KeyArtBlobs.OptionPath(_worldId, chosen)));
            Assert.That(afterChoose.KeyArt.Brief, Is.EqualTo(Brief));
            Assert.That(afterChoose.ChosenOptionId, Is.EqualTo(chosen));
            Assert.That(_blobs.DeletedPaths, Does.Contain(KeyArtBlobs.OptionPath(_worldId, unchosen)));
            Assert.That(_blobs.DeletedPaths, Does.Not.Contain(KeyArtBlobs.OptionPath(_worldId, chosen)));
            Assert.That(_blobs.Blobs.Keys, Has.Count.EqualTo(3), "the chosen art plus one generation's options");
            Assert.That(second.KeyArt!.BlobPath, Is.EqualTo(KeyArtBlobs.OptionPath(_worldId, chosen)));
            Assert.That(second.ChosenOptionId, Is.Null, "the art is no longer among the options on offer");
        });

        // Choosing one of the new options lets the old art go: still at most three blobs.
        await ChooseAsync(second.Options[0]);
        Assert.Multiple(() =>
        {
            Assert.That(_blobs.DeletedPaths, Does.Contain(KeyArtBlobs.OptionPath(_worldId, chosen)));
            Assert.That(_blobs.Blobs.Keys, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Choose_AnOptionNotOnOffer_IsNotFound()
    {
        var result = await ChooseAsync(Guid.NewGuid());

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Upload_TakesAPngOrJpeg_ReadFromItsBytes_AndWarnsWhenNarrow()
    {
        var wide = (await UploadAsync(TestImages.Jpeg(2000, 1000))).Value!;
        var narrow = (await UploadAsync(TestImages.Png(800, 400))).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(wide.Warning, Is.Null);
            Assert.That(narrow.Warning, Does.Contain("800 pixels wide"));
            Assert.That(narrow.KeyArt!.Source, Is.EqualTo(CoverSource.Uploaded));
            Assert.That(narrow.KeyArt.ContentType, Is.EqualTo("image/png"));
            Assert.That(narrow.KeyArt.BlobPath, Does.EndWith(".png"));
            Assert.That(_blobs.Blobs.Keys, Has.Count.EqualTo(1), "the replaced upload is deleted");
        });
    }

    [Test]
    public async Task Upload_RefusesWhatIsNotAnImage_AndWhatIsTooLarge()
    {
        _options.MaxUploadBytes = 64;
        var text = await UploadAsync("GIF89a not really an image"u8.ToArray());
        var empty = await UploadAsync([]);
        var large = await UploadAsync(new byte[65]);

        Assert.Multiple(() =>
        {
            Assert.That(text.Error!.Code, Is.EqualTo("invalid_image"));
            Assert.That(empty.Error!.Code, Is.EqualTo("invalid_image"));
            Assert.That(large.Error!.StatusCode, Is.EqualTo(413));
            Assert.That(_blobs.Blobs, Is.Empty);
        });
    }

    [Test]
    public async Task TheAnchor_IsKept_AndCarriesAcrossANewPicture()
    {
        await UploadAsync(TestImages.Png(2000, 1000));
        var anchored = (await _sut.SetAnchorAsync(new SetKeyArtAnchorCommand(_worldId, GmUserId, WorldRole.GM, KeyArtAnchor.Top), default)).Value!;
        var replaced = (await UploadAsync(TestImages.Png(2000, 1000))).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(anchored.KeyArt!.Anchor, Is.EqualTo(KeyArtAnchor.Top));
            Assert.That(replaced.KeyArt!.Anchor, Is.EqualTo(KeyArtAnchor.Top));
        });
    }

    [Test]
    public async Task TheAnchor_WithNoArt_IsAConflict()
    {
        var result = await _sut.SetAnchorAsync(new SetKeyArtAnchorCommand(_worldId, GmUserId, WorldRole.GM, KeyArtAnchor.Top), default);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task Remove_DeletesAnUpload_ButLeavesAnOptionOnOffer()
    {
        await UploadAsync(TestImages.Png(2000, 1000));
        var uploadPath = Stored.KeyArt!.BlobPath;
        await _sut.RemoveAsync(new RemoveKeyArtCommand(_worldId, GmUserId, WorldRole.GM), default);

        Assert.Multiple(() =>
        {
            Assert.That(Stored.KeyArt, Is.Null);
            Assert.That(_blobs.DeletedPaths, Does.Contain(uploadPath));
        });

        var options = (await GenerateAsync()).Value!.Options;
        await ChooseAsync(options[0]);
        var removed = (await _sut.RemoveAsync(new RemoveKeyArtCommand(_worldId, GmUserId, WorldRole.GM), default)).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(removed.KeyArt, Is.Null);
            Assert.That(removed.Options, Is.EqualTo(options), "the option can be chosen again");
            Assert.That(_blobs.Blobs.Keys, Does.Contain(KeyArtBlobs.OptionPath(_worldId, options[0])));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task EveryWrite_IsTheGms(WorldRole role)
    {
        var results = new[]
        {
            await _sut.GetAsync(_worldId, GmUserId, role, default),
            await _sut.SaveBriefAsync(new SaveKeyArtBriefCommand(_worldId, GmUserId, role, Brief), default),
            await GenerateAsync(role: role),
            await _sut.ChooseAsync(new ChooseKeyArtCommand(_worldId, GmUserId, role, Guid.NewGuid()), default),
            await UploadAsync(TestImages.Png(2000, 1000), role),
            await _sut.SetAnchorAsync(new SetKeyArtAnchorCommand(_worldId, GmUserId, role, KeyArtAnchor.Top), default),
            await _sut.RemoveAsync(new RemoveKeyArtCommand(_worldId, GmUserId, role), default),
        };

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.Error?.StatusCode), Is.All.EqualTo(403));
            Assert.That(_generator.Calls, Is.Empty);
            Assert.That(_blobs.Blobs, Is.Empty);
        });
    }

    [Test]
    public async Task TheArt_IsEveryMembersToSee_ButThePublicOnlyWhenTheWorldIsPublic()
    {
        await UploadAsync(TestImages.Png(2000, 1000));

        var member = await _sut.OpenAsync(_worldId, default);
        var hidden = await _sut.OpenForPublicAsync(Stored, default);
        Stored.PublicAccessEnabled = true;
        var shown = await _sut.OpenForPublicAsync(Stored, default);

        Assert.Multiple(() =>
        {
            Assert.That(member.IsSuccess, Is.True);
            Assert.That(member.Value!.ContentType, Is.EqualTo("image/png"));
            Assert.That(hidden.Error!.StatusCode, Is.EqualTo(404));
            Assert.That(shown.IsSuccess, Is.True);
        });
    }

    [Test]
    public async Task AnOption_IsTheGmsAlone_AndOnlyWhileOnOffer()
    {
        var option = (await GenerateAsync()).Value!.Options[0];

        var gm = await _sut.OpenOptionAsync(_worldId, option, WorldRole.GM, default);
        var player = await _sut.OpenOptionAsync(_worldId, option, WorldRole.Player, default);
        var stranger = await _sut.OpenOptionAsync(_worldId, Guid.NewGuid(), WorldRole.GM, default);

        Assert.Multiple(() =>
        {
            Assert.That(gm.IsSuccess, Is.True);
            Assert.That(player.Error!.StatusCode, Is.EqualTo(403));
            Assert.That(stranger.Error!.StatusCode, Is.EqualTo(404));
        });
    }

    [Test]
    public async Task TheBrief_IsSaved_AndBoundedAtACoversLength()
    {
        var saved = (await _sut.SaveBriefAsync(new SaveKeyArtBriefCommand(_worldId, GmUserId, WorldRole.GM, "  " + Brief + "  "), default)).Value!;
        var tooLong = await _sut.SaveBriefAsync(
            new SaveKeyArtBriefCommand(_worldId, GmUserId, WorldRole.GM, new string('a', Nornis.Domain.Models.WorldKeyArt.MaxBriefChars + 1)), default);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Brief, Is.EqualTo(Brief));
            Assert.That(tooLong.Error!.Code, Is.EqualTo("invalid_brief"));
        });
    }

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
