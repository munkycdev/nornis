using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Which source types the ordinary create and update paths accept, by role. A GM note is the
/// GM's own voice to the extractor; before this rule a Player could POST <c>type: GMNote</c>
/// and the UI's GM-only button was the only thing standing in the way. Reveal and
/// LibraryExcerpt are minted by their own services and must not be forgeable through capture.
/// </summary>
[TestFixture]
public class SourceServiceCaptureTypeTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private SourceService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _sut = new SourceService(_sourceRepository, new InMemoryWorldMemberRepository(), new InMemoryCampaignRepository(),
            new FakeExtractionQueueClient(), new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(),
            new InMemorySourceAttachmentRepository(), new FakeBlobStorageService(), NullLogger<SourceService>.Instance);
    }

    private Task<AppResult<Source>> CreateAsync(SourceType type, WorldRole role) =>
        _sut.CreateAsync(new CreateSourceCommand(
            WorldId, "Session 1", type, VisibilityScope.PartyVisible, UserId, role, Body: "We met Captain Voss."),
            CancellationToken.None);

    private Task<AppResult<Source>> UpdateTypeAsync(Guid id, WorldRole role, SourceType type, string? title = null) =>
        _sut.UpdateAsync(new UpdateSourceCommand(id, WorldId, UserId, role, Title: title, Type: type),
            CancellationToken.None);

    private async Task<Guid> SeedAsync(SourceType type)
    {
        var source = await _sourceRepository.CreateAsync(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = type,
            Title = "Minted",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = UserId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Draft
        }, CancellationToken.None);
        return source.Id;
    }

    [Test]
    [Category("Authorization")]
    public async Task Create_PlayerCannotCreateAGmNote()
    {
        var result = await CreateAsync(SourceType.GMNote, WorldRole.Player);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(await _sourceRepository.ListByWorldAsync(WorldId, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task Create_GmCanCreateAGmNote()
    {
        var result = await CreateAsync(SourceType.GMNote, WorldRole.GM);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Type, Is.EqualTo(SourceType.GMNote));
    }

    [Test]
    public async Task Create_PlayerCanStillCreateASessionNote()
    {
        var result = await CreateAsync(SourceType.SessionNote, WorldRole.Player);

        Assert.That(result.IsSuccess, Is.True);
    }

    [TestCase(SourceType.Reveal)]
    [TestCase(SourceType.LibraryExcerpt)]
    [Category("Authorization")]
    public async Task Create_NoRoleCanCaptureAMintedType(SourceType type)
    {
        var result = await CreateAsync(type, WorldRole.GM);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    [Category("Authorization")]
    public async Task Update_PlayerCannotRetypeTheirSourceToAGmNote()
    {
        var created = await CreateAsync(SourceType.SessionNote, WorldRole.Player);

        var result = await UpdateTypeAsync(created.Value!.Id, WorldRole.Player, SourceType.GMNote);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        var stored = await _sourceRepository.GetByIdAsync(created.Value.Id, CancellationToken.None);
        Assert.That(stored!.Type, Is.EqualTo(SourceType.SessionNote));
    }

    [Test]
    [Category("Authorization")]
    public async Task Update_AMintedSourceCannotBeRetyped()
    {
        var id = await SeedAsync(SourceType.Reveal);

        var result = await UpdateTypeAsync(id, WorldRole.GM, SourceType.SessionNote);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Update_ResendingAnUnchangedMintedTypeIsNotARetype()
    {
        // Clients resend every field on save. The rule is about changing the type, so a GM
        // renaming a reveal record must not trip it just because "Reveal" came back too.
        var id = await SeedAsync(SourceType.Reveal);

        var result = await UpdateTypeAsync(id, WorldRole.GM, SourceType.Reveal, title: "Renamed");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Title, Is.EqualTo("Renamed"));
    }
}
