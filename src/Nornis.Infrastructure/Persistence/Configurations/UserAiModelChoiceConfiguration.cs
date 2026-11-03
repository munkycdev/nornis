using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class UserAiModelChoiceConfiguration : IEntityTypeConfiguration<UserAiModelChoice>
{
    public void Configure(EntityTypeBuilder<UserAiModelChoice> builder)
    {
        builder.ToTable("UserAiModelChoices");

        builder.HasKey(c => new { c.UserId, c.Task });

        builder.Property(c => c.Task)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(c => c.ModelId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
