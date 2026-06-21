using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("Sources");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Body)
            .HasColumnType("nvarchar(max)");

        builder.Property(s => s.Uri)
            .HasMaxLength(2000);

        builder.Property(s => s.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.Visibility)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.ProcessingStatus)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.OccurredAt)
            .HasColumnType("datetimeoffset");

        builder.Property(s => s.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(s => new { s.CampaignId, s.ProcessingStatus });

        builder.HasOne(s => s.Campaign)
            .WithMany()
            .HasForeignKey(s => s.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.CreatedByUser)
            .WithMany()
            .HasForeignKey(s => s.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
