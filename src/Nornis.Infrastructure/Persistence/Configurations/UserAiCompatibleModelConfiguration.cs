using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class UserAiCompatibleModelConfiguration : IEntityTypeConfiguration<UserAiCompatibleModel>
{
    public void Configure(EntityTypeBuilder<UserAiCompatibleModel> builder)
    {
        builder.ToTable("UserAiCompatibleModels");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Task)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(m => m.ModelName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.InputPerMillionUsd)
            .HasPrecision(10, 4);

        builder.Property(m => m.OutputPerMillionUsd)
            .HasPrecision(10, 4);

        builder.HasIndex(m => new { m.UserAiKeyId, m.Task })
            .IsUnique();
    }
}
