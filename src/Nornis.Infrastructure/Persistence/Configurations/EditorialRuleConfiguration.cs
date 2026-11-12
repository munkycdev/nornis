using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class EditorialRuleConfiguration : IEntityTypeConfiguration<EditorialRule>
{
    public void Configure(EntityTypeBuilder<EditorialRule> builder)
    {
        builder.ToTable("EditorialRules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Text)
            .IsRequired()
            .HasMaxLength(EditorialRule.MaxTextChars);

        builder.Property(r => r.Origin)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.CreatedAt)
            .HasColumnType("datetimeoffset");

        // Always read whole, per world, on every extraction.
        builder.HasIndex(r => r.WorldId);

        builder.HasOne(r => r.World)
            .WithMany()
            .HasForeignKey(r => r.WorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
