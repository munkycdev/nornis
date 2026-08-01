using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Body and Uri clearing on <see cref="SourceService.UpdateAsync"/>. The update is partial,
/// so null means "no change" — which left an emptied editor silently reverting under a
/// success toast, because there was no way to say "I meant to empty it". Same idiom as
/// ClearOccurredAt, added for the same reason.
/// </summary>
[TestFixture]
public class SourceServiceClearBodyUriTests
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

    private async Task<Guid> CreateSourceAsync(string? body = "We met Captain Voss.", string? uri = null)
    {
        var created = await _sut.CreateAsync(new CreateSourceCommand(
            WorldId, "Session 1", SourceType.SessionNote, VisibilityScope.PartyVisible,
            UserId, WorldRole.GM, Body: body, Uri: uri), CancellationToken.None);
        return created.Value!.Id;
    }

    private Task<Nornis.Application.Errors.AppResult<Nornis.Domain.Entities.Source>> UpdateAsync(
        Guid id, bool clearBody = false, bool clearUri = false, string? title = null) =>
        _sut.UpdateAsync(
            new UpdateSourceCommand(id, WorldId, UserId, WorldRole.GM,
                Title: title, ClearBody: clearBody, ClearUri: clearUri),
            CancellationToken.None);

    [Test]
    public async Task ClearBody_EmptiesTheBody()
    {
        var id = await CreateSourceAsync();

        var result = await UpdateAsync(id, clearBody: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Body, Is.Null);
    }

    [Test]
    public async Task NullBody_LeavesTheBodyUnchanged()
    {
        var id = await CreateSourceAsync();

        // The regression this whole change exists for: without the flag, this is exactly
        // what an emptied editor sent, and the server read it as "unchanged".
        var result = await UpdateAsync(id, title: "Renamed");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Body, Is.EqualTo("We met Captain Voss."));
    }

    [Test]
    public async Task ClearUri_EmptiesTheUri()
    {
        var id = await CreateSourceAsync(uri: "https://example.test/notes");

        var result = await UpdateAsync(id, clearUri: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Uri, Is.Null);
    }

    [Test]
    public async Task NullUri_LeavesTheUriUnchanged()
    {
        var id = await CreateSourceAsync(uri: "https://example.test/notes");

        var result = await UpdateAsync(id, title: "Renamed");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Uri, Is.EqualTo("https://example.test/notes"));
    }

    [Test]
    public async Task ClearBody_OnAnUnextractedProcessedSource_IsAllowed()
    {
        // No review batch exists for this source, so nothing derived is at risk and the
        // reprocess gate must not fire.
        var id = await CreateSourceAsync();
        var source = (await _sourceRepository.GetByIdAsync(id))!;
        source.ProcessingStatus = SourceProcessingStatus.Processed;

        var result = await UpdateAsync(id, clearBody: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Body, Is.Null);
    }
}
