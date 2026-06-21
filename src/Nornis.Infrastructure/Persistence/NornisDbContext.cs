using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence;

public class NornisDbContext : DbContext
{
    public NornisDbContext(DbContextOptions<NornisDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignMember> CampaignMembers => Set<CampaignMember>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceExtraction> SourceExtractions => Set<SourceExtraction>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<ArtifactFact> ArtifactFacts => Set<ArtifactFact>();
    public DbSet<ArtifactRelationship> ArtifactRelationships => Set<ArtifactRelationship>();
    public DbSet<SourceReference> SourceReferences => Set<SourceReference>();
    public DbSet<ReviewBatch> ReviewBatches => Set<ReviewBatch>();
    public DbSet<ReviewProposal> ReviewProposals => Set<ReviewProposal>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NornisDbContext).Assembly);
    }
}
