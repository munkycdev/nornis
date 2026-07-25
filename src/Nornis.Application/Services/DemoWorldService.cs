using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Creates demo worlds from the template package — a world-export zip curated from the
/// master demo campaign (feature 20). Instantiation is a snapshot copy, not a re-extraction:
/// every row gets a fresh id (loose references included), every actor column collapses to
/// the creating user, blobs are copied to world-scoped paths, and session dates shift so
/// the timeline reads as recent. The DB write is one transaction via
/// <see cref="IWorldImportWriter"/>.
/// </summary>
public class DemoWorldService : IDemoWorldService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>How far in the past the latest session lands, so the world feels lived-in
    /// but current.</summary>
    private static readonly TimeSpan LatestSessionAge = TimeSpan.FromDays(3);

    private static readonly string[] FallbackAdjectives =
        ["Ashen", "Sundered", "Gilded", "Drowned", "Emberlit", "Thornbound", "Veiled", "Wintered", "Salt-Worn", "Starlit"];

    private static readonly string[] FallbackNouns =
        ["Reach", "Coast", "Vale", "Marches", "Expanse", "Dominion", "Shores", "Hinterland", "Crossing", "Frontier"];

    private readonly IWorldRepository _worldRepository;
    private readonly IWorldImportWriter _importWriter;
    private readonly IDemoWorldTemplateProvider _templateProvider;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IWorldNameGenerator _nameGenerator;
    private readonly DemoWorldOptions _options;
    private readonly ILogger<DemoWorldService> _logger;

    public DemoWorldService(
        IWorldRepository worldRepository,
        IWorldImportWriter importWriter,
        IDemoWorldTemplateProvider templateProvider,
        IBlobStorageService blobStorageService,
        IWorldNameGenerator nameGenerator,
        IOptions<DemoWorldOptions> options,
        ILogger<DemoWorldService> logger)
    {
        _worldRepository = worldRepository;
        _importWriter = importWriter;
        _templateProvider = templateProvider;
        _blobStorageService = blobStorageService;
        _nameGenerator = nameGenerator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AppResult<World>> CreateAsync(CreateDemoWorldCommand command, CancellationToken ct)
    {
        if (!_templateProvider.IsAvailable)
        {
            return AppResult<World>.Fail(new AppError(503, "demo_unavailable",
                "The demo world is not available right now."));
        }

        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var recent = await _worldRepository.CountDemoWorldsCreatedSinceAsync(command.ActingUserId, since, ct);
        if (recent >= _options.MaxCreationsPerUserPerDay)
        {
            return AppResult<World>.Fail(new AppError(429, "demo_rate_limited",
                "You've created a demo world recently — try again tomorrow."));
        }

        TemplatePackage package;
        try
        {
            await using var zipStream = _templateProvider.OpenRead();
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            package = await TemplatePackage.ParseAsync(zip, ct);

            var name = await GenerateNameAsync(ct);
            var (world, member, rows) = await MaterializeAsync(package, zip, command, name, ct);

            await _importWriter.WriteAsync(world, member, rows, ct);

            _logger.LogInformation(
                "Demo world {WorldId} (\"{Name}\") created for user {UserId}: {Sources} sources, {Artifacts} artifacts, {Attachments} attachments",
                world.Id, world.Name, command.ActingUserId,
                rows.Sources.Count, rows.Artifacts.Count, rows.Attachments.Count);

            return AppResult<World>.Success(world);
        }
        catch (InvalidTemplateException ex)
        {
            _logger.LogError(ex, "Demo world template package is invalid");
            return AppResult<World>.Fail(new AppError(500, "invalid_template",
                "The demo world template could not be read."));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            _logger.LogError(ex, "Demo world template package could not be parsed");
            return AppResult<World>.Fail(new AppError(500, "invalid_template",
                "The demo world template could not be read."));
        }
    }

    private async Task<string> GenerateNameAsync(CancellationToken ct)
    {
        string? generated = null;
        try
        {
            generated = await _nameGenerator.GenerateAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "World name generation failed; using a fallback name");
        }

        generated = generated?.Trim();
        if (!string.IsNullOrEmpty(generated))
        {
            return generated.Length <= 200 ? generated : generated[..200];
        }

        return $"The {FallbackAdjectives[Random.Shared.Next(FallbackAdjectives.Length)]} " +
               $"{FallbackNouns[Random.Shared.Next(FallbackNouns.Length)]}";
    }

    // ---------------------------------------------------------------- materialization --

    private async Task<(World World, WorldMember Member, WorldExportData Rows)> MaterializeAsync(
        TemplatePackage package,
        ZipArchive zip,
        CreateDemoWorldCommand command,
        string name,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var worldId = Guid.NewGuid();
        var userId = command.ActingUserId;

        var world = new World
        {
            Id = worldId,
            Name = name,
            Description = package.World?.Description,
            GameSystem = package.World?.GameSystem,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDemo = true,
            TutorialEnabled = command.TutorialEnabled,
        };

        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            Role = WorldRole.GM,
            JoinedAt = now,
        };

        // One old→new map across every entity space: ids are globally unique GUIDs, so a
        // single dictionary also catches template corruption (duplicate ids) at build time.
        var ids = new IdMap();
        package.Campaigns.ForEach(c => ids.Register(c.Id));
        package.Characters.ForEach(c => ids.Register(c.Id));
        package.Sources.ForEach(s => ids.Register(s.Id));
        package.Attachments.ForEach(a => ids.Register(a.Id));
        package.Artifacts.ForEach(a => ids.Register(a.Id));
        package.Facts.ForEach(f => ids.Register(f.Id));
        package.Relationships.ForEach(r => ids.Register(r.Id));

        // Shift session dates so the latest lands a few days ago, preserving spacing.
        var occurred = package.Sources.Where(s => s.OccurredAt is not null).Select(s => s.OccurredAt!.Value).ToList();
        var delta = occurred.Count > 0 ? now - LatestSessionAge - occurred.Max() : TimeSpan.Zero;

        var sources = package.Sources.Select(s => new Source
        {
            Id = ids.Map(s.Id),
            WorldId = worldId,
            CampaignId = s.CampaignId is { } cid ? ids.Map(cid) : null,
            Type = s.Type,
            Title = s.Title,
            Body = s.Body,
            Uri = s.Uri,
            OccurredAt = s.OccurredAt + delta,
            CreatedAt = now,
            CreatedByUserId = userId,
            Visibility = s.Visibility,
            ProcessingStatus = s.ProcessingStatus,
            ExtractionEnabled = s.ExtractionEnabled,
            DerivedText = s.DerivedText,
        }).ToList();

        var attachments = new List<SourceAttachment>();
        foreach (var a in package.Attachments)
        {
            var attachment = new SourceAttachment
            {
                Id = ids.Map(a.Id),
                SourceId = ids.Map(a.SourceId),
                WorldId = worldId,
                Kind = a.Kind,
                FileName = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                Ord = a.Ord,
                Status = a.Status,
                CreatedAt = now,
                UpdatedAt = now,
            };

            if (a.Status == SourceAttachmentStatus.Stored)
            {
                if (a.File is null || zip.GetEntry(a.File) is not { } entry)
                {
                    throw new InvalidTemplateException($"Template is missing the file for attachment '{a.FileName}'.");
                }

                attachment.BlobPath = _blobStorageService.BuildSourceBlobPath(worldId, attachment.SourceId, a.FileName);
                await using var entryStream = entry.Open();
                await _blobStorageService.UploadAsync(attachment.BlobPath, entryStream, a.ContentType, ct);
            }

            attachments.Add(attachment);
        }

        // References to review proposals are dropped by design: the Reviews category is not
        // part of the template, so those targets don't exist in the copy.
        var droppedReferences = package.References.Count(r => r.TargetType == SourceReferenceTargetType.ReviewProposal);
        if (droppedReferences > 0)
        {
            _logger.LogInformation("Demo import dropped {Count} review-proposal references", droppedReferences);
        }

        var rows = new WorldExportData
        {
            Campaigns = package.Campaigns.Select(c => new Campaign
            {
                Id = ids.Map(c.Id),
                WorldId = worldId,
                Name = c.Name,
                Description = c.Description,
                Status = c.Status,
                StartedAt = c.StartedAt + delta,
                EndedAt = c.EndedAt + delta,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = userId,
            }).ToList(),

            Characters = package.Characters.Select(c => new Character
            {
                Id = ids.Map(c.Id),
                WorldId = worldId,
                WorldMemberId = member.Id,
                Name = c.Name,
                Description = c.Description,
                ArtifactId = c.ArtifactId is { } aid ? ids.Map(aid) : null,
                CreatedAt = now,
                UpdatedAt = now,
            }).ToList(),

            CampaignCharacters = package.CampaignCharacters.Select(cc => new CampaignCharacter
            {
                Id = Guid.NewGuid(),
                CampaignId = ids.Map(cc.CampaignId),
                CharacterId = ids.Map(cc.CharacterId),
                CreatedAt = now,
            }).ToList(),

            StorylineCampaigns = package.StorylineCampaigns.Select(sc => new StorylineCampaign
            {
                Id = Guid.NewGuid(),
                ArtifactId = ids.Map(sc.ArtifactId),
                CampaignId = ids.Map(sc.CampaignId),
                CreatedAt = now,
                CreatedByUserId = userId,
            }).ToList(),

            Sources = sources,
            Attachments = attachments,

            SourceExtractions = package.Extractions.Select(e => new SourceExtraction
            {
                Id = Guid.NewGuid(),
                SourceId = ids.Map(e.SourceId),
                ExtractionType = e.ExtractionType,
                Text = e.Text,
                Confidence = e.Confidence,
                CreatedAt = now,
            }).ToList(),

            SourceReferences = package.References
                .Where(r => r.TargetType != SourceReferenceTargetType.ReviewProposal)
                .Select(r => new SourceReference
                {
                    Id = Guid.NewGuid(),
                    SourceId = ids.Map(r.SourceId),
                    TargetType = r.TargetType,
                    TargetId = ids.Map(r.TargetId),
                    Quote = r.Quote,
                    Notes = r.Notes,
                    CreatedAt = now,
                }).ToList(),

            Artifacts = package.Artifacts.Select(a => new Artifact
            {
                Id = ids.Map(a.Id),
                WorldId = worldId,
                Type = a.Type,
                Name = a.Name,
                Summary = a.Summary,
                Visibility = a.Visibility,
                Confidence = a.Confidence,
                Status = a.Status,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = userId,
            }).ToList(),

            ArtifactFacts = package.Facts.Select(f => new ArtifactFact
            {
                Id = ids.Map(f.Id),
                ArtifactId = ids.Map(f.ArtifactId),
                Predicate = f.Predicate,
                Value = f.Value,
                Confidence = f.Confidence,
                TruthState = f.TruthState,
                Visibility = f.Visibility,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = userId,
            }).ToList(),

            ArtifactRelationships = package.Relationships.Select(r => new ArtifactRelationship
            {
                Id = ids.Map(r.Id),
                WorldId = worldId,
                ArtifactAId = ids.Map(r.ArtifactAId),
                ArtifactBId = ids.Map(r.ArtifactBId),
                Type = r.Type,
                Description = r.Description,
                Confidence = r.Confidence,
                TruthState = r.TruthState,
                Visibility = r.Visibility,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = userId,
            }).ToList(),

            MapPlacemarks = package.Pins.Select(p => new MapPlacemark
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                SourceAttachmentId = ids.Map(p.SourceAttachmentId),
                ArtifactId = ids.Map(p.ArtifactId),
                X = p.X,
                Y = p.Y,
                Label = p.Label,
                Confidence = p.Confidence,
                CreatedAt = now,
                UpdatedAt = now,
            }).ToList(),
        };

        return (world, member, rows);
    }

    // ------------------------------------------------------------------------ parsing --

    private sealed class IdMap
    {
        private readonly Dictionary<Guid, Guid> _map = [];

        public void Register(Guid oldId)
        {
            if (!_map.TryAdd(oldId, Guid.NewGuid()))
            {
                throw new InvalidTemplateException($"Template contains duplicate id {oldId}.");
            }
        }

        public Guid Map(Guid oldId) => _map.TryGetValue(oldId, out var fresh)
            ? fresh
            : throw new InvalidTemplateException($"Template references unknown id {oldId}.");
    }

    private sealed class InvalidTemplateException(string message) : Exception(message);

    private sealed record TemplatePackage(
        WorldDoc? World,
        List<CampaignDoc> Campaigns,
        List<CampaignCharacterDoc> CampaignCharacters,
        List<StorylineCampaignDoc> StorylineCampaigns,
        List<CharacterDoc> Characters,
        List<SourceDoc> Sources,
        List<ExtractionDoc> Extractions,
        List<ReferenceDoc> References,
        List<AttachmentDoc> Attachments,
        List<ArtifactDoc> Artifacts,
        List<FactDoc> Facts,
        List<RelationshipDoc> Relationships,
        List<PinDoc> Pins)
    {
        public static async Task<TemplatePackage> ParseAsync(ZipArchive zip, CancellationToken ct)
        {
            var world = await ReadEntryAsync<WorldDoc>(zip, "world.json", ct);
            var campaigns = await ReadEntryAsync<CampaignsDoc>(zip, "campaigns.json", ct);
            var sources = await ReadEntryAsync<SourcesDoc>(zip, "sources.json", ct);
            var codex = await ReadEntryAsync<CodexDoc>(zip, "codex.json", ct);

            return new TemplatePackage(
                World: world,
                Campaigns: campaigns?.Campaigns ?? [],
                CampaignCharacters: campaigns?.CampaignCharacters ?? [],
                StorylineCampaigns: campaigns?.StorylineCampaigns ?? [],
                Characters: await ReadEntryAsync<List<CharacterDoc>>(zip, "characters.json", ct) ?? [],
                Sources: sources?.Sources ?? [],
                Extractions: sources?.Extractions ?? [],
                References: sources?.References ?? [],
                Attachments: await ReadEntryAsync<List<AttachmentDoc>>(zip, "attachments.json", ct) ?? [],
                Artifacts: codex?.Artifacts ?? [],
                Facts: codex?.Facts ?? [],
                Relationships: codex?.Relationships ?? [],
                Pins: await ReadEntryAsync<List<PinDoc>>(zip, "map-pins.json", ct) ?? []);
        }

        private static async Task<T?> ReadEntryAsync<T>(ZipArchive zip, string entryName, CancellationToken ct)
        {
            if (zip.GetEntry(entryName) is not { } entry)
            {
                return default;
            }

            await using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        }
    }

    // Wire shapes mirror WorldExportService's serialized fields, camelCase on disk.
    private sealed record WorldDoc(string? Name, string? Description, string? GameSystem);

    private sealed record CampaignsDoc(
        List<CampaignDoc>? Campaigns, List<CampaignCharacterDoc>? CampaignCharacters, List<StorylineCampaignDoc>? StorylineCampaigns);

    private sealed record CampaignDoc(
        Guid Id, string Name, string? Description, CampaignStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt);

    private sealed record CampaignCharacterDoc(Guid Id, Guid CampaignId, Guid CharacterId);

    private sealed record StorylineCampaignDoc(Guid Id, Guid ArtifactId, Guid CampaignId);

    private sealed record CharacterDoc(Guid Id, Guid WorldMemberId, string Name, string? Description, Guid? ArtifactId);

    private sealed record SourcesDoc(List<SourceDoc>? Sources, List<ExtractionDoc>? Extractions, List<ReferenceDoc>? References);

    private sealed record CodexDoc(List<ArtifactDoc>? Artifacts, List<FactDoc>? Facts, List<RelationshipDoc>? Relationships);

    private sealed record SourceDoc(
        Guid Id, Guid? CampaignId, SourceType Type, string Title, string? Body, string? Uri,
        DateTimeOffset? OccurredAt, VisibilityScope Visibility, SourceProcessingStatus ProcessingStatus,
        bool ExtractionEnabled, string? DerivedText);

    private sealed record ExtractionDoc(
        Guid Id, Guid SourceId, SourceExtractionType ExtractionType, string Text, decimal? Confidence);

    private sealed record ReferenceDoc(
        Guid Id, Guid SourceId, SourceReferenceTargetType TargetType, Guid TargetId, string? Quote, string? Notes);

    private sealed record AttachmentDoc(
        Guid Id, Guid SourceId, SourceAttachmentKind Kind, string FileName, string ContentType,
        long SizeBytes, int Ord, SourceAttachmentStatus Status, string? File);

    private sealed record ArtifactDoc(
        Guid Id, ArtifactType Type, string Name, string? Summary, VisibilityScope Visibility,
        decimal? Confidence, ArtifactStatus Status);

    private sealed record FactDoc(
        Guid Id, Guid ArtifactId, string Predicate, string Value, decimal? Confidence,
        TruthState TruthState, VisibilityScope Visibility);

    private sealed record RelationshipDoc(
        Guid Id, Guid ArtifactAId, Guid ArtifactBId, string Type, string? Description,
        decimal? Confidence, TruthState TruthState, VisibilityScope Visibility);

    private sealed record PinDoc(
        Guid Id, Guid SourceAttachmentId, Guid ArtifactId, decimal X, decimal Y, string? Label, decimal? Confidence);
}
