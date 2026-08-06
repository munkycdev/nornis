using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class WorldDigestConfiguration : IEntityTypeConfiguration<WorldDigest>
{
    public void Configure(EntityTypeBuilder<WorldDigest> builder)
    {
        builder.ToTable("WorldDigests");

        builder.HasKey(d => d.Id);

        // One digest per world — the row is the record; UpsertAsync replaces it in place
        // and the index makes a concurrent-insert race a constraint violation instead of
        // a silent second row.
        builder.HasIndex(d => d.WorldId)
            .IsUnique();

        builder.Property(d => d.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.GeneratedAt)
            .HasColumnType("datetimeoffset");

        builder.HasOne(d => d.World)
            .WithMany()
            .HasForeignKey(d => d.WorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
