using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// File to Library for a Prep print (feature 29, Requirement 7.2–7.5): the server, not the
/// client, makes it a GM-only, dated Handout that records its Adventure, and each print is a
/// new document that becomes the Adventure's last print.
/// </summary>
[TestFixture]
public class LibraryPrintTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    private InMemoryLibraryDocumentRepository _documents = null!;
    private FakeBlobStorageService _blobs = null!;
    private InMemoryAdventureRepository _adventures = null!;
    private LibraryService _sut = null!;
    private Adventure _adventure = null!;

    [SetUp]
    public async Task SetUp()
    {
        _documents = new InMemoryLibraryDocumentRepository();
        _blobs = new FakeBlobStorageService();
        _adventures = new InMemoryAdventureRepository();
        _sut = new LibraryService(_documents, new InMemoryLibraryChunkRepository(), _blobs,
            new FakeLibraryIndexingQueueClient(), _adventures, Options.Create(new LibraryOptions()),
            NullLogger<LibraryService>.Instance);
        _adventure = await SeedAdventureAsync(WorldId, "The Silver Key");
    }

    private async Task<Adventure> SeedAdventureAsync(Guid worldId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return await _adventures.CreateAsync(new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Title = title,
            CreatedByUserId = GmId,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static RequestLibraryUploadCommand Print(Guid adventureId, WorldRole role = WorldRole.GM) =>
        new(WorldId, GmId, role, "Anything the browser sent", "booklet.pdf", "application/pdf", 2048,
            LibraryDocumentKind.Sourcebook, VisibilityScope.PartyVisible, adventureId);

    private async Task<LibraryDocument> FileAsync()
    {
        var ticket = (await _sut.RequestUploadAsync(Print(_adventure.Id), CancellationToken.None)).Value!;
        _blobs.Blobs[ticket.Document.BlobPath] = (new byte[2048], "application/pdf");
        var confirmed = await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.Error?.Message);
        return confirmed.Value!;
    }

    [Test]
    public async Task APrint_IsAGmOnlyDatedHandout_ThatRecordsItsAdventure_WhateverTheClientAsked()
    {
        var result = await _sut.RequestUploadAsync(Print(_adventure.Id), CancellationToken.None);

        var document = result.Value!.Document;
        Assert.Multiple(() =>
        {
            Assert.That(document.Kind, Is.EqualTo(LibraryDocumentKind.Handout));
            Assert.That(document.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(document.Title, Is.EqualTo($"The Silver Key, printed {DateTimeOffset.UtcNow:yyyy-MM-dd}"));
            Assert.That(document.AdventureId, Is.EqualTo(_adventure.Id));
        });
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task OnlyTheGm_CanFileAPrint(WorldRole role)
    {
        var result = await _sut.RequestUploadAsync(Print(_adventure.Id, role), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(403));
        Assert.That(_documents.Documents, Is.Empty);
    }

    [Test]
    public async Task AnotherWorldsAdventure_IsNotFound()
    {
        var elsewhere = await SeedAdventureAsync(Guid.NewGuid(), "Elsewhere");

        var result = await _sut.RequestUploadAsync(Print(elsewhere.Id), CancellationToken.None);

        Assert.That(result.Error?.StatusCode, Is.EqualTo(404));
        Assert.That(_documents.Documents, Is.Empty);
    }

    [Test]
    public async Task EachPrint_IsANewDocument_AndTheNewestIsTheLastPrint()
    {
        var first = await FileAsync();
        var second = await FileAsync();

        var adventure = await _adventures.GetWithSectionsAsync(_adventure.Id);
        Assert.Multiple(() =>
        {
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            Assert.That(_documents.Documents.Select(d => d.Id), Is.SupersetOf(new[] { first.Id, second.Id }));
            Assert.That(adventure!.LastPrintedDocumentId, Is.EqualTo(second.Id));
        });
    }

    [Test]
    public async Task AnOrdinaryUpload_TouchesNoAdventure()
    {
        var ticket = (await _sut.RequestUploadAsync(
            Print(_adventure.Id) with { AdventureId = null, Title = "Sourcebook" }, CancellationToken.None)).Value!;
        _blobs.Blobs[ticket.Document.BlobPath] = (new byte[2048], "application/pdf");
        await _sut.ConfirmUploadAsync(ticket.Document.Id, WorldId, GmId, CancellationToken.None);

        Assert.That(ticket.Document.AdventureId, Is.Null);
        Assert.That(ticket.Document.Kind, Is.EqualTo(LibraryDocumentKind.Sourcebook));
        Assert.That((await _adventures.GetWithSectionsAsync(_adventure.Id))!.LastPrintedDocumentId, Is.Null);
    }

    [Test]
    public void PrintTitle_FitsTheTitleBound_AndKeepsTheDate()
    {
        var title = LibraryService.PrintTitle(new string('x', 200), new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));

        Assert.That(title, Has.Length.LessThanOrEqualTo(200));
        Assert.That(title, Does.EndWith(", printed 2026-11-14"));
    }
}
