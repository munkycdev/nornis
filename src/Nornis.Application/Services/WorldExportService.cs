using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nornis.Application.Authorization;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class WorldExportService : IWorldExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IWorldRepository _worldRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly IWorldExportReader _exportReader;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<WorldExportService> _logger;

    public WorldExportService(
        IWorldRepository worldRepository,
        IWorldMemberRepository worldMemberRepository,
        IWorldExportReader exportReader,
        IBlobStorageService blobStorageService,
        ILogger<WorldExportService> logger)
    {
        _worldRepository = worldRepository;
        _worldMemberRepository = worldMemberRepository;
        _exportReader = exportReader;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<AppResult<WorldExportResult>> ExportAsync(ExportWorldCommand command, CancellationToken ct)
    {
        var member = await _worldMemberRepository.GetByWorldAndUserAsync(command.WorldId, command.ActingUserId, ct);

        if (member is null || !member.Role.IsAtLeast(WorldRole.GM))
        {
            return AppResult<WorldExportResult>.Fail(new AppError(403, "insufficient_role", "Only a GM can export a world."));
        }

        var world = await _worldRepository.GetByIdAsync(command.WorldId, ct);

        if (world is null)
        {
            return AppResult<WorldExportResult>.Fail(new AppError(404, "not_found", "World not found."));
        }

        var categories = command.Categories.ToHashSet();

        if (categories.Count == 0)
        {
            return AppResult<WorldExportResult>.Fail(new AppError(400, "no_categories", "Select at least one data type to export."));
        }

        var data = await _exportReader.ReadAsync(command.WorldId, categories, ct);

        var exportedAt = DateTimeOffset.UtcNow;
        var fileName = $"{Slugify(world.Name)}-export-{exportedAt:yyyyMMdd-HHmmss}.zip";
        var blobPath = $"worlds/{command.WorldId}/exports/{fileName}";

        // The zip is staged in a temp file, not memory — library PDFs and page scans can
        // run to hundreds of megabytes.
        var tempPath = Path.Combine(Path.GetTempPath(), $"nornis-export-{Guid.NewGuid():N}.zip");

        try
        {
            await using (var zipStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                await WriteZipAsync(zip, world, categories, data, exportedAt, ct);
            }

            var sizeBytes = new FileInfo(tempPath).Length;

            // Each world keeps only its latest export. Clearing first is best-effort — a
            // failed sweep leaves stale zips behind (removed with the world eventually),
            // which must not block producing a fresh one.
            try
            {
                await _blobStorageService.DeleteByPrefixAsync($"worlds/{command.WorldId}/exports/", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clear earlier exports for world {WorldId}", command.WorldId);
            }

            await using (var readStream = File.OpenRead(tempPath))
            {
                await _blobStorageService.UploadAsync(blobPath, readStream, "application/zip", ct);
            }

            var downloadUrl = await _blobStorageService.GenerateDownloadSasUrlAsync(blobPath, ct);

            _logger.LogInformation(
                "World {WorldId} (\"{WorldName}\") exported by user {UserId}: {Categories} → {FileName} ({SizeBytes} bytes)",
                command.WorldId, world.Name, command.ActingUserId,
                string.Join(",", categories.Order()), fileName, sizeBytes);

            return AppResult<WorldExportResult>.Success(new WorldExportResult(downloadUrl, fileName, sizeBytes));
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // A leaked temp file is not worth failing the export over.
            }
        }
    }

    private async Task WriteZipAsync(
        ZipArchive zip,
        World world,
        IReadOnlySet<WorldExportCategory> categories,
        WorldExportData data,
        DateTimeOffset exportedAt,
        CancellationToken ct)
    {
        // Blobs that vanished out from under their rows (interrupted uploads, manual
        // cleanup) are skipped and confessed to in the manifest — one lost file must
        // not sink the whole export.
        var missingFiles = new List<string>();

        await WriteJsonEntryAsync(zip, "world.json", new
        {
            world.Id,
            world.Name,
            world.Description,
            world.GameSystem,
            world.CreatedAt,
            world.UpdatedAt,
            world.CreatedByUserId,
            world.PublicSlug,
            world.PublicAccessEnabled,
            world.DailyAiBudgetUsd,
            world.PublicAskMonthlyBudgetUsd,
        }, ct);

        if (categories.Contains(WorldExportCategory.Members))
        {
            await WriteJsonEntryAsync(zip, "members.json", data.Members.Select(m => new
            {
                m.Id, m.UserId, m.Role, m.DisplayName, m.JoinedAt,
            }), ct);
        }

        if (categories.Contains(WorldExportCategory.Campaigns))
        {
            await WriteJsonEntryAsync(zip, "campaigns.json", new
            {
                Campaigns = data.Campaigns.Select(c => new
                {
                    c.Id, c.Name, c.Description, c.Status, c.StartedAt, c.EndedAt,
                    c.CreatedAt, c.UpdatedAt, c.CreatedByUserId,
                }),
                CampaignCharacters = data.CampaignCharacters.Select(cc => new
                {
                    cc.Id, cc.CampaignId, cc.CharacterId, cc.CreatedAt,
                }),
                StorylineCampaigns = data.StorylineCampaigns.Select(sc => new
                {
                    sc.Id, sc.ArtifactId, sc.CampaignId, sc.CreatedAt, sc.CreatedByUserId,
                }),
            }, ct);
        }

        if (categories.Contains(WorldExportCategory.Characters))
        {
            await WriteJsonEntryAsync(zip, "characters.json", data.Characters.Select(c => new
            {
                c.Id, c.WorldMemberId, c.Name, c.Description, c.ArtifactId, c.CreatedAt, c.UpdatedAt,
            }), ct);
        }

        if (categories.Contains(WorldExportCategory.Sources))
        {
            await WriteJsonEntryAsync(zip, "sources.json", new
            {
                Sources = data.Sources.Select(s => new
                {
                    s.Id, s.CampaignId, s.Type, s.Title, s.Body, s.Uri, s.OccurredAt,
                    s.CreatedAt, s.CreatedByUserId, s.Visibility, s.ProcessingStatus,
                    s.ExtractionEnabled, s.DerivedText,
                }),
                Extractions = data.SourceExtractions.Select(e => new
                {
                    e.Id, e.SourceId, e.ExtractionType, e.Text, e.Confidence, e.CreatedAt,
                }),
                References = data.SourceReferences.Select(r => new
                {
                    r.Id, r.SourceId, r.TargetType, r.TargetId, r.Quote, r.Notes, r.CreatedAt,
                }),
            }, ct);
        }

        if (categories.Contains(WorldExportCategory.Attachments))
        {
            await WriteJsonEntryAsync(zip, "attachments.json", data.Attachments.Select(a => new
            {
                a.Id, a.SourceId, a.Kind, a.FileName, a.ContentType, a.SizeBytes,
                a.Ord, a.Status, a.CreatedAt, a.UpdatedAt,
                File = a.Status == SourceAttachmentStatus.Stored ? AttachmentEntryName(a) : null,
            }), ct);

            foreach (var attachment in data.Attachments.Where(a => a.Status == SourceAttachmentStatus.Stored))
            {
                await CopyBlobEntryAsync(zip, attachment.BlobPath, AttachmentEntryName(attachment), missingFiles, ct);
            }
        }

        if (categories.Contains(WorldExportCategory.Codex))
        {
            await WriteJsonEntryAsync(zip, "codex.json", new
            {
                Artifacts = data.Artifacts.Select(a => new
                {
                    a.Id, a.Type, a.Name, a.Summary, a.Visibility, a.Confidence,
                    a.Status, a.CreatedAt, a.UpdatedAt, a.CreatedByUserId,
                }),
                Facts = data.ArtifactFacts.Select(f => new
                {
                    f.Id, f.ArtifactId, f.Predicate, f.Value, f.Confidence, f.TruthState,
                    f.Visibility, f.CreatedAt, f.UpdatedAt, f.CreatedByUserId,
                }),
                Relationships = data.ArtifactRelationships.Select(r => new
                {
                    r.Id, r.ArtifactAId, r.ArtifactBId, r.Type, r.Description, r.Confidence,
                    r.TruthState, r.Visibility, r.CreatedAt, r.UpdatedAt, r.CreatedByUserId,
                }),
            }, ct);
        }

        if (categories.Contains(WorldExportCategory.MapPins))
        {
            await WriteJsonEntryAsync(zip, "map-pins.json", data.MapPlacemarks.Select(p => new
            {
                p.Id, p.SourceAttachmentId, p.ArtifactId, p.X, p.Y, p.Label,
                p.Confidence, p.CreatedAt, p.UpdatedAt,
            }), ct);
        }

        if (categories.Contains(WorldExportCategory.Library))
        {
            await WriteJsonEntryAsync(zip, "library.json", data.LibraryDocuments.Select(d => new
            {
                d.Id, d.Title, d.FileName, d.ContentType, d.SizeBytes, d.Kind,
                d.Visibility, d.Status, d.PageCount, d.UploadedByUserId, d.CreatedAt, d.UpdatedAt,
                File = d.Status != LibraryDocumentStatus.PendingUpload ? LibraryEntryName(d) : null,
            }), ct);

            foreach (var document in data.LibraryDocuments.Where(d => d.Status != LibraryDocumentStatus.PendingUpload))
            {
                await CopyBlobEntryAsync(zip, document.BlobPath, LibraryEntryName(document), missingFiles, ct);
            }
        }

        if (categories.Contains(WorldExportCategory.Reviews))
        {
            await WriteJsonEntryAsync(zip, "reviews.json", new
            {
                Batches = data.ReviewBatches.Select(b => new
                {
                    b.Id, b.SourceId, b.Status, b.Kind, b.CreatedAt, b.CompletedAt,
                }),
                Proposals = data.ReviewProposals.Select(p => new
                {
                    p.Id, p.ReviewBatchId, p.ChangeType, p.TargetType, p.TargetId,
                    p.ProposedValueJson, p.Rationale, p.Confidence, p.Status,
                    p.CreatedAt, p.ReviewedAt, p.ReviewedByUserId,
                }),
            }, ct);
        }

        if (categories.Contains(WorldExportCategory.Health))
        {
            await WriteJsonEntryAsync(zip, "health.json", new
            {
                Assessments = data.HealthAssessments.Select(h => new
                {
                    h.Id, h.CreatedAt, h.Model, h.Score,
                }),
                Findings = data.ContinuityFindings.Select(f => new
                {
                    f.Id, f.HealthAssessmentId, f.Category, f.Severity, f.Summary,
                    f.SuggestedAction, f.EvidenceJson, f.ArtifactId, f.Status,
                }),
            }, ct);
        }

        if (categories.Contains(WorldExportCategory.AiUsage))
        {
            await WriteJsonEntryAsync(zip, "ai-usage.json", data.AiUsageRecords.Select(a => new
            {
                a.Id, a.UserId, a.OperationType, a.Model, a.InputTokens, a.OutputTokens,
                a.TotalTokens, a.EstimatedCostUsd, a.SourceId, a.ReviewBatchId,
                a.DurationMs, a.Succeeded, a.ErrorCode, a.CreatedAt,
            }), ct);
        }

        // Written last so it can list the files that turned out to be missing, but named
        // manifest.json so it reads first.
        await WriteJsonEntryAsync(zip, "manifest.json", new
        {
            FormatVersion = 1,
            WorldId = world.Id,
            WorldName = world.Name,
            ExportedAt = exportedAt,
            Categories = categories.Order().Select(c => c.ToString()),
            MissingFiles = missingFiles,
        }, ct);
    }

    private static string AttachmentEntryName(SourceAttachment attachment) =>
        $"attachments/{attachment.SourceId}/{attachment.Id}/{attachment.FileName}";

    private static string LibraryEntryName(LibraryDocument document) =>
        $"library/{document.Id}/{document.FileName}";

    private static async Task WriteJsonEntryAsync<T>(ZipArchive zip, string entryName, T value, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
    }

    /// <summary>Copies a blob into the zip, or records it as missing when it no longer exists.
    /// Stored files (images, PDFs) are usually pre-compressed, hence Fastest.</summary>
    private async Task CopyBlobEntryAsync(
        ZipArchive zip,
        string blobPath,
        string entryName,
        List<string> missingFiles,
        CancellationToken ct)
    {
        Stream blobStream;
        try
        {
            blobStream = await _blobStorageService.OpenReadAsync(blobPath, ct);
        }
        catch (FileNotFoundException)
        {
            missingFiles.Add(entryName);
            return;
        }

        await using (blobStream)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await blobStream.CopyToAsync(entryStream, ct);
        }
    }

    /// <summary>Lowercased ASCII letters/digits with dashes — a filename-safe world name.</summary>
    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');

        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }

        return slug.Length == 0 ? "world" : slug;
    }
}
