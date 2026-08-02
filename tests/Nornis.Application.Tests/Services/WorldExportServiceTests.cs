using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class WorldExportServiceTests
{
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private FakeWorldExportReader _reader = null!;
    private FakeBlobStorageService _blobs = null!;
    private WorldExportService _sut = null!;
    private World _world = null!;
    private Guid _gmId;
    private Guid _playerId;

    [SetUp]
    public void SetUp()
    {
        _worlds = new InMemoryWorldRepository();
        _members = new InMemoryWorldMemberRepository();
        _reader = new FakeWorldExportReader();
        _blobs = new FakeBlobStorageService();
        _sut = new WorldExportService(_worlds, _members, _reader, _blobs, NullLogger<WorldExportService>.Instance);

        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();

        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Black Harbor",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _worlds.CreateAsync(_world).GetAwaiter().GetResult();
        _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = _gmId,
            Role = WorldRole.GM,
            JoinedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
        _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = _playerId,
            Role = WorldRole.Player,
            JoinedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
    }

    private ExportWorldCommand Command(
        IReadOnlyCollection<WorldExportCategory>? categories = null,
        Guid? actingUserId = null) =>
        new(_world.Id, actingUserId ?? _gmId, categories ?? Enum.GetValues<WorldExportCategory>());

    private byte[] UploadedZipBytes()
    {
        var path = _blobs.Blobs.Keys.Single(k => k.StartsWith($"worlds/{_world.Id}/exports/", StringComparison.Ordinal));
        return _blobs.Blobs[path].Content;
    }

    private static ZipArchive OpenZip(byte[] bytes) =>
        new(new MemoryStream(bytes), ZipArchiveMode.Read);

    private static JsonDocument ReadJsonEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        Assert.That(entry, Is.Not.Null, $"zip entry {entryName} missing");
        using var stream = entry!.Open();
        return JsonDocument.Parse(stream);
    }

    [Test]
    public async Task Export_AsGm_UploadsZipAndReturnsDownloadUrl()
    {
        var sourceId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        _reader.Data = new WorldExportData
        {
            Sources = [new Source { Id = sourceId, WorldId = _world.Id, Title = "Session 1", Body = "We sailed." }],
            Attachments =
            [
                new SourceAttachment
                {
                    Id = attachmentId,
                    SourceId = sourceId,
                    WorldId = _world.Id,
                    FileName = "page-1.png",
                    ContentType = "image/png",
                    SizeBytes = 4,
                    BlobPath = $"worlds/{_world.Id}/sources/{sourceId}/page-1.png",
                    Status = SourceAttachmentStatus.Stored,
                },
            ],
        };
        _blobs.Blobs[$"worlds/{_world.Id}/sources/{sourceId}/page-1.png"] = ([1, 2, 3, 4], "image/png");

        var result = await _sut.ExportAsync(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var export = result.Value!;
        Assert.That(export.FileName, Does.StartWith("black-harbor-export-").And.EndWith(".zip"));
        Assert.That(export.SizeBytes, Is.GreaterThan(0));

        var blobPath = $"worlds/{_world.Id}/exports/{export.FileName}";
        Assert.That(_blobs.Blobs.ContainsKey(blobPath), Is.True);
        Assert.That(_blobs.Blobs[blobPath].ContentType, Is.EqualTo("application/zip"));
        Assert.That(export.DownloadUrl, Is.EqualTo($"https://blob.test/{blobPath}?sas=download"));

        using var zip = OpenZip(UploadedZipBytes());

        using var world = ReadJsonEntry(zip, "world.json");
        Assert.That(world.RootElement.GetProperty("name").GetString(), Is.EqualTo("Black Harbor"));

        using var sources = ReadJsonEntry(zip, "sources.json");
        var source = sources.RootElement.GetProperty("sources").EnumerateArray().Single();
        Assert.That(source.GetProperty("title").GetString(), Is.EqualTo("Session 1"));

        var fileEntry = zip.GetEntry($"attachments/{sourceId}/{attachmentId}/page-1.png");
        Assert.That(fileEntry, Is.Not.Null);
        using var fileStream = new MemoryStream();
        using (var entryStream = fileEntry!.Open())
        {
            entryStream.CopyTo(fileStream);
        }
        Assert.That(fileStream.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

        using var manifest = ReadJsonEntry(zip, "manifest.json");
        Assert.That(manifest.RootElement.GetProperty("formatVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(manifest.RootElement.GetProperty("worldName").GetString(), Is.EqualTo("Black Harbor"));
        Assert.That(manifest.RootElement.GetProperty("missingFiles").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task Export_SelectedCategoriesOnly_WritesOnlyThoseFiles()
    {
        _reader.Data = new WorldExportData
        {
            Members = [new WorldMember { Id = Guid.NewGuid(), WorldId = _world.Id, UserId = _gmId, Role = WorldRole.GM }],
            Artifacts = [new Artifact { Id = Guid.NewGuid(), WorldId = _world.Id, Name = "The Drowned Bell" }],
        };

        var result = await _sut.ExportAsync(Command([WorldExportCategory.Codex]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_reader.LastCategories, Is.EquivalentTo(new[] { WorldExportCategory.Codex }));

        using var zip = OpenZip(UploadedZipBytes());
        Assert.That(zip.GetEntry("codex.json"), Is.Not.Null);
        Assert.That(zip.GetEntry("world.json"), Is.Not.Null);
        Assert.That(zip.GetEntry("members.json"), Is.Null);
        Assert.That(zip.GetEntry("sources.json"), Is.Null);

        using var codex = ReadJsonEntry(zip, "codex.json");
        var artifact = codex.RootElement.GetProperty("artifacts").EnumerateArray().Single();
        Assert.That(artifact.GetProperty("name").GetString(), Is.EqualTo("The Drowned Bell"));

        using var manifest = ReadJsonEntry(zip, "manifest.json");
        var categories = manifest.RootElement.GetProperty("categories").EnumerateArray()
            .Select(c => c.GetString()).ToList();
        Assert.That(categories, Is.EqualTo(new[] { "Codex" }));
    }

    [Test]
    public async Task Export_MissingAttachmentBlob_ListsItInManifestAndSucceeds()
    {
        var sourceId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        _reader.Data = new WorldExportData
        {
            Attachments =
            [
                new SourceAttachment
                {
                    Id = attachmentId,
                    SourceId = sourceId,
                    WorldId = _world.Id,
                    FileName = "lost.png",
                    BlobPath = $"worlds/{_world.Id}/sources/{sourceId}/lost.png",
                    Status = SourceAttachmentStatus.Stored,
                },
            ],
        };

        var result = await _sut.ExportAsync(Command([WorldExportCategory.Attachments]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        using var zip = OpenZip(UploadedZipBytes());
        var expectedEntry = $"attachments/{sourceId}/{attachmentId}/lost.png";
        Assert.That(zip.GetEntry(expectedEntry), Is.Null);

        using var manifest = ReadJsonEntry(zip, "manifest.json");
        var missing = manifest.RootElement.GetProperty("missingFiles").EnumerateArray()
            .Select(m => m.GetString()).ToList();
        Assert.That(missing, Is.EqualTo(new[] { expectedEntry }));
    }

    [Test]
    public async Task Export_PendingUploadAttachment_MetadataOnlyNoFile()
    {
        var sourceId = Guid.NewGuid();
        _reader.Data = new WorldExportData
        {
            Attachments =
            [
                new SourceAttachment
                {
                    Id = Guid.NewGuid(),
                    SourceId = sourceId,
                    WorldId = _world.Id,
                    FileName = "pending.png",
                    BlobPath = $"worlds/{_world.Id}/sources/{sourceId}/pending.png",
                    Status = SourceAttachmentStatus.PendingUpload,
                },
            ],
        };

        var result = await _sut.ExportAsync(Command([WorldExportCategory.Attachments]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        using var zip = OpenZip(UploadedZipBytes());
        using var attachments = ReadJsonEntry(zip, "attachments.json");
        var attachment = attachments.RootElement.EnumerateArray().Single();
        Assert.That(attachment.GetProperty("file").ValueKind, Is.EqualTo(JsonValueKind.Null));

        using var manifest = ReadJsonEntry(zip, "manifest.json");
        Assert.That(manifest.RootElement.GetProperty("missingFiles").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task Export_ClearsEarlierExportsFirst()
    {
        _blobs.Blobs[$"worlds/{_world.Id}/exports/stale-export.zip"] = ([9, 9], "application/zip");

        var result = await _sut.ExportAsync(Command([WorldExportCategory.Members]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_blobs.DeletedPrefixes, Is.EqualTo(new[] { $"worlds/{_world.Id}/exports/" }));
        Assert.That(_blobs.Blobs.Keys, Has.None.Contain("stale-export.zip"));
        Assert.That(_blobs.Blobs.ContainsKey($"worlds/{_world.Id}/exports/{result.Value!.FileName}"), Is.True);
    }

    [Test]
    public async Task Export_NoCategories_Returns400()
    {
        var result = await _sut.ExportAsync(Command([]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("no_categories"));
        Assert.That(_blobs.Blobs, Is.Empty);
    }

    [Test]

    [Category("Authorization")]
    public async Task Export_AsPlayer_Returns403()
    {
        var result = await _sut.ExportAsync(Command(actingUserId: _playerId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(result.Error.Code, Is.EqualTo("insufficient_role"));
        Assert.That(_blobs.Blobs, Is.Empty);
    }

    [Test]

    [Category("Authorization")]
    public async Task Export_AsNonMember_Returns403()
    {
        var result = await _sut.ExportAsync(Command(actingUserId: Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Export_WorldGoneButMembershipLingers_Returns404()
    {
        await _worlds.DeleteAsync(_world.Id);

        var result = await _sut.ExportAsync(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Export_StaleExportSweepFails_StillProducesTheExport()
    {
        _blobs.FailDeletes = true;

        var result = await _sut.ExportAsync(Command([WorldExportCategory.Members]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, "clearing earlier exports is best-effort");
        Assert.That(_blobs.Blobs.ContainsKey($"worlds/{_world.Id}/exports/{result.Value!.FileName}"), Is.True);
    }
}
