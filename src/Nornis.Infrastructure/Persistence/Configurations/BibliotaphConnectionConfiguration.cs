using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class BibliotaphConnectionConfiguration : IEntityTypeConfiguration<BibliotaphConnection>
{
    public void Configure(EntityTypeBuilder<BibliotaphConnection> builder)
    {
        builder.ToTable("BibliotaphConnections");

        builder.HasKey(c => c.Id);

        // Data Protection ciphertext of an Auth0 refresh token; a few hundred characters.
        builder.Property(c => c.ProtectedRefreshToken)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.ConnectedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.StatusChangedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.LastUsedAt)
            .HasColumnType("datetimeoffset");

        // One connection per user: connecting again replaces the grant.
        builder.HasIndex(c => c.UserId)
            .IsUnique();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
