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
    public DbSet<ContinuityFinding> ContinuityFindings => Set<ContinuityFinding>();
    public DbSet<LibraryDocument> LibraryDocuments => Set<LibraryDocument>();
    public DbSet<LibraryChunk> LibraryChunks => Set<LibraryChunk>();
    public DbSet<MapPlacemark> MapPlacemarks => Set<MapPlacemark>();

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
