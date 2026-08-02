using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class OperationalFlagConfiguration : IEntityTypeConfiguration<OperationalFlag>
{
    public void Configure(EntityTypeBuilder<OperationalFlag> builder)
    {
        builder.ToTable("OperationalFlags");

        // The name is the key: one row per flag, so flipping one twice overwrites rather
        // than accumulates, and "which row is current" is never a question.
        builder.HasKey(f => f.Name);

        builder.Property(f => f.Name)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(f => f.Reason)
            .HasMaxLength(500);

        builder.Property(f => f.UpdatedAt)
            .HasColumnType("datetimeoffset");
    }
}
