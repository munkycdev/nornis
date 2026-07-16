using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class LibraryChunkConfiguration : IEntityTypeConfiguration<LibraryChunk>
{
    /// <summary>text-embedding-3-small output width; must match the embedding client.</summary>
    public const int EmbeddingDimensions = 1536;

    /// <summary>Shadow-property name for the vector — the domain entity never sees provider types.</summary>
    public const string EmbeddingProperty = "Embedding";

    public void Configure(EntityTypeBuilder<LibraryChunk> builder)
    {
        builder.ToTable("LibraryChunks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        // The embedding shadow property is added in NornisDbContext.OnModelCreating,
        // gated to the SQL Server provider — Sqlite/InMemory test providers have no
        // native vector type.

        // Similarity search scans per world; document join resolves titles + visibility.
        builder.HasIndex(c => c.WorldId);
        builder.HasIndex(c => c.DocumentId);

        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
