using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class UserAiKeyConfiguration : IEntityTypeConfiguration<UserAiKey>
{
    public void Configure(EntityTypeBuilder<UserAiKey> builder)
    {
        builder.ToTable("UserAiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(k => k.Label)
            .IsRequired()
            .HasMaxLength(100);

        // Data Protection ciphertext of the key; a few hundred characters.
        builder.Property(k => k.ProtectedKey)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(k => k.LastFour)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(k => k.BaseUrl)
            .HasMaxLength(500);

        builder.Property(k => k.AddedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(k => k.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(k => k.CheckedAt)
            .HasColumnType("datetimeoffset");

        // One key per provider: saving another replaces it.
        builder.HasIndex(k => new { k.UserId, k.Provider })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(k => k.CompatibleModels)
            .WithOne()
            .HasForeignKey(m => m.UserAiKeyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
