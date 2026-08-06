using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Infrastructure.Persistence.Configurations;

namespace Nornis.Infrastructure.Persistence;

public class NornisDbContext : DbContext
{
    public NornisDbContext(DbContextOptions<NornisDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<World> Worlds => Set<World>();
    public DbSet<WorldMember> WorldMembers => Set<WorldMember>();
    public DbSet<WorldInvite> WorldInvites => Set<WorldInvite>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CampaignCharacter> CampaignCharacters => Set<CampaignCharacter>();
    public DbSet<StorylineCampaign> StorylineCampaigns => Set<StorylineCampaign>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceAttachment> SourceAttachments => Set<SourceAttachment>();
    public DbSet<SourceExtraction> SourceExtractions => Set<SourceExtraction>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<ArtifactFact> ArtifactFacts => Set<ArtifactFact>();
    public DbSet<ArtifactRelationship> ArtifactRelationships => Set<ArtifactRelationship>();
    public DbSet<SourceReference> SourceReferences => Set<SourceReference>();
    public DbSet<ReviewBatch> ReviewBatches => Set<ReviewBatch>();
    public DbSet<ReviewProposal> ReviewProposals => Set<ReviewProposal>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<HealthAssessment> HealthAssessments => Set<HealthAssessment>();

    public DbSet<WorldDigest> WorldDigests => Set<WorldDigest>();
    public DbSet<ContinuityFinding> ContinuityFindings => Set<ContinuityFinding>();
    public DbSet<ContinuityDismissal> ContinuityDismissals => Set<ContinuityDismissal>();
    public DbSet<LibraryDocument> LibraryDocuments => Set<LibraryDocument>();
    public DbSet<LibraryChunk> LibraryChunks => Set<LibraryChunk>();
    public DbSet<MapPlacemark> MapPlacemarks => Set<MapPlacemark>();
    public DbSet<ExtractionReplay> ExtractionReplays => Set<ExtractionReplay>();
    public DbSet<ImportSession> ImportSessions => Set<ImportSession>();

    public DbSet<ImportSessionItem> ImportSessionItems => Set<ImportSessionItem>();
    public DbSet<TutorialProgress> TutorialProgress => Set<TutorialProgress>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    public DbSet<OperationalFlag> OperationalFlags => Set<OperationalFlag>();

    /// <summary>
    /// Stamps <see cref="Source.StatusChangedAt"/> whenever a source's processing status is
    /// actually modified.
    ///
    /// Here rather than at the call sites because there are thirty-eight of them across nine
    /// services, and the column is what a safety gate reads: the Queued wedge's only route out
    /// is "has this been stuck long enough that no delivery can still be in flight". One call
    /// site forgetting to stamp would not fail a test — it would silently make a wedged source
    /// look fresh forever, which is the bug, restored.
    ///
    /// The change tracker knows precisely which property changed, so this stamps on real
    /// transitions and not on unrelated saves. It cannot see <c>ExecuteUpdate</c>, which
    /// bypasses tracking entirely — <see cref="Repositories.SourceRepository.TryClaimForExtractionAsync"/>
    /// sets the column in its own SetProperty for that reason.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Source>())
        {
            var isNewlyQueued = entry.State == EntityState.Added;
            var statusChanged = entry.State == EntityState.Modified
                && entry.Property(s => s.ProcessingStatus).IsModified;

            if (isNewlyQueued || statusChanged)
            {
                entry.Entity.StatusChangedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NornisDbContext).Assembly);

        // The chunk embedding is a SQL Server-native vector column; Sqlite/InMemory test
        // providers have no such type, so it exists only on the real provider. Repository
        // vector paths (Replace/Search) therefore require SQL Server.
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<LibraryChunk>()
                .Property<SqlVector<float>>(LibraryChunkConfiguration.EmbeddingProperty)
                .HasColumnType($"vector({LibraryChunkConfiguration.EmbeddingDimensions})")
                .IsRequired();
        }
    }
}
