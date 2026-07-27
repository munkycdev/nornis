using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ImportSessionConfiguration : IEntityTypeConfiguration<ImportSession>
{
    public void Configure(EntityTypeBuilder<ImportSession> builder)
    {
        builder.ToTable("ImportSessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(s => s.UpdatedAt)
            .HasColumnType("datetimeoffset");

        // The one query that matters: "this world's non-terminal import session" — and the
        // filtered uniqueness is the real guarantee behind the service's 409, so two racing
        // creates cannot both land a Draft session the UI can then never reach.
        builder.HasIndex(s => s.WorldId)
            .IsUnique()
            .HasFilter("[Status] IN ('Draft', 'InProgress')")
            .HasDatabaseName("IX_ImportSessions_WorldId_NonTerminal");

        builder.HasOne(s => s.World)
            .WithMany()
            .HasForeignKey(s => s.WorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
